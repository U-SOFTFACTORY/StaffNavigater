using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// スタッフナビゲーター（業務管理システム）から手動出力したCSVの読込・検証・取込
    /// </summary>
    public class CsvImportService(LaborRepository repo)
    {
        public static readonly string[] AttendanceHeaders = ["スタッフ番号", "氏名", "拠点", "勤務日", "勤務区分", "出勤時刻", "退勤時刻", "休憩(分)", "備考"];
        public static readonly string[] StaffHeaders = ["スタッフ番号", "氏名", "フリガナ", "性別", "生年月日", "拠点", "雇用区分", "入社日", "時給", "通勤手当日額", "住民税月額", "扶養家族人数"];

        /// <summary>省略できる項目（スタッフ情報）</summary>
        private static readonly string[] OptionalStaffHeaders = ["生年月日", "扶養家族人数"];

        /// <summary>以前の項目名も読めるようにする</summary>
        private static readonly Dictionary<string, string> HeaderAliases = new() { ["社員番号"] = "スタッフ番号" };

        private static readonly string[] DateFormats = ["yyyy/M/d", "yyyy-M-d", "yyyyMMdd"];
        private static readonly string[] TimeFormats = ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"];

        public PendingImport Parse(string fileName, byte[] bytes, string user)
        {
            var (text, encoding) = Decode(bytes);
            var pending = new PendingImport
            {
                Token = Guid.NewGuid().ToString("N"),
                FileName = Path.GetFileName(fileName),
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                Encoding = encoding,
                UploadedBy = user,
                UploadedAt = Clock.Now,
            };
            var rows = CsvText.Parse(text);
            if (rows.Count == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "CSVにデータがありません。");
                return pending;
            }

            var header = rows[0].Select(h => h.Trim()).Select(h => HeaderAliases.GetValueOrDefault(h, h)).ToList();
            if (header.Contains("勤務日"))
            {
                pending.Kind = ImportKind.Attendance;
                ParseAttendance(pending, header, rows);
            }
            else if (header.Contains("時給"))
            {
                pending.Kind = ImportKind.Staff;
                ParseStaff(pending, header, rows);
            }
            else
            {
                AddIssue(pending, 1, ImportIssue.Error, "CSVの形式を判別できません。1行目に項目名（勤怠実績CSV／スタッフ情報CSV）が必要です。");
                return pending;
            }

            var duplicate = repo.GetBatches().FirstOrDefault(b => b.Sha256 == pending.Sha256);
            if (duplicate != null)
            {
                pending.Notices.Add($"同一内容のファイルが {Fmt.DateTime(duplicate.ImportedAt)} に取込済みです（{duplicate.FileName}）。");
            }
            return pending;
        }

        public ImportBatch Commit(PendingImport pending, string user, DateTimeOffset? at = null)
        {
            if (!pending.CanCommit)
            {
                throw new InvalidOperationException("エラーがあるため取込できません。");
            }
            var now = at ?? Clock.Now;
            var batch = new ImportBatch
            {
                Id = $"B{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                Kind = pending.Kind!.Value,
                FileName = pending.FileName,
                Sha256 = pending.Sha256,
                Encoding = pending.Encoding,
                ImportedAt = now,
                ImportedBy = user,
                TargetMonth = pending.TargetMonth,
                RowCount = pending.Kind == ImportKind.Attendance ? pending.Data.Records.Count : pending.Data.Staff.Count,
                WarningCount = pending.WarningCount,
            };
            repo.AddBatch(batch, pending.Data);
            var target = batch.TargetMonth != null ? $"対象月：{Fmt.Month(batch.TargetMonth)}、" : "";
            repo.AddLog(user, "CSV取込", $"{batch.Kind.Label()}CSV「{batch.FileName}」を取り込みました（{target}{batch.RowCount}件、警告{batch.WarningCount}件）", now);
            return batch;
        }

        private void ParseAttendance(PendingImport pending, List<string> header, List<string[]> rows)
        {
            var index = MapHeader(pending, header, AttendanceHeaders.Where(h => h != "備考"));
            if (index == null)
            {
                return;
            }
            var noteIndex = header.IndexOf("備考");
            var staff = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            if (staff.Count == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "スタッフ情報が未登録です。先にスタッフ情報CSVを取り込んでください。");
            }
            var settings = repo.GetSettings();
            var seen = new HashSet<(string, DateOnly)>();

            for (var i = 1; i < rows.Count; i++)
            {
                var line = i + 1;
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                string Get(string name) => Cell(row, index[name]);

                var employeeNo = Get("スタッフ番号");
                var record = new AttendanceRecord
                {
                    LineNo = line,
                    EmployeeNo = employeeNo,
                    Name = Get("氏名"),
                    Site = Get("拠点"),
                    Note = noteIndex >= 0 ? NullIfEmpty(Cell(row, noteIndex)) : null,
                };
                var errors = new List<string>();
                if (employeeNo == "")
                {
                    errors.Add("スタッフ番号が未入力です。");
                }
                else if (staff.Count > 0 && !staff.ContainsKey(employeeNo))
                {
                    errors.Add($"スタッフ番号「{employeeNo}」がスタッフ情報に存在しません。");
                }
                if (DateOnly.TryParseExact(Get("勤務日"), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    record.Date = date;
                }
                else
                {
                    errors.Add($"勤務日「{Get("勤務日")}」の形式が正しくありません。");
                }
                if (DayKindExtensions.TryParse(Get("勤務区分"), out var kind))
                {
                    record.Kind = kind;
                }
                else
                {
                    errors.Add($"勤務区分「{Get("勤務区分")}」は使用できません（出勤／休日出勤／有給／公休／欠勤）。");
                }
                record.Start = ParseTime(Get("出勤時刻"), "出勤時刻", errors);
                record.End = ParseTime(Get("退勤時刻"), "退勤時刻", errors);
                var breakText = Get("休憩(分)");
                if (breakText == "")
                {
                    record.BreakMinutes = 0;
                }
                else if (int.TryParse(breakText, out var breakMinutes) && breakMinutes >= 0)
                {
                    record.BreakMinutes = breakMinutes;
                }
                else
                {
                    errors.Add($"休憩(分)「{breakText}」は0以上の数値で入力してください。");
                }
                if (errors.Count == 0 && !seen.Add((employeeNo, record.Date)))
                {
                    errors.Add($"同じスタッフ・勤務日の行が重複しています（{record.Date:yyyy/MM/dd}）。");
                }

                foreach (var error in errors)
                {
                    AddIssue(pending, line, ImportIssue.Error, error, employeeNo, record.Date == default ? null : record.Date);
                }
                if (errors.Count > 0)
                {
                    continue;
                }
                var calc = LaborCalculator.Calculate(record.Values, settings);
                foreach (var warning in LaborCalculator.Validate(record.Values, calc))
                {
                    AddIssue(pending, line, ImportIssue.Warning, warning, employeeNo, record.Date);
                }
                pending.Data.Records.Add(record);
            }

            var months = pending.Data.Records.Select(r => r.Date.ToString("yyyy-MM")).Distinct().ToList();
            if (months.Count > 1)
            {
                AddIssue(pending, 0, ImportIssue.Error, $"複数の月のデータが含まれています（{string.Join("、", months.Select(Fmt.Month))}）。1ファイル1か月分で出力してください。");
            }
            else if (months.Count == 1)
            {
                pending.TargetMonth = months[0];
                var existing = repo.GetBatches()
                    .Where(b => b.Kind == ImportKind.Attendance && b.TargetMonth == months[0])
                    .OrderByDescending(b => b.ImportedAt)
                    .FirstOrDefault();
                if (existing != null)
                {
                    pending.Notices.Add($"{Fmt.Month(months[0])}分は {Fmt.DateTime(existing.ImportedAt)} に取込済みです。" +
                        "取込を確定すると、今回のデータを最新として使用します（以前の取込データと補正履歴は削除されず保持されます）。");
                }
            }
            else if (pending.ErrorCount == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "取込対象の行がありません。");
            }
        }

        private static void ParseStaff(PendingImport pending, List<string> header, List<string[]> rows)
        {
            var index = MapHeader(pending, header, StaffHeaders.Except(OptionalStaffHeaders));
            if (index == null)
            {
                return;
            }
            var birthIndex = header.IndexOf("生年月日");
            var dependentsIndex = header.IndexOf("扶養家族人数");
            var seen = new HashSet<string>();
            for (var i = 1; i < rows.Count; i++)
            {
                var line = i + 1;
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                string Get(string name) => Cell(row, index[name]);

                var errors = new List<string>();
                var staff = new StaffMember
                {
                    EmployeeNo = Get("スタッフ番号"),
                    Name = Get("氏名"),
                    Kana = Get("フリガナ"),
                    Gender = Get("性別"),
                    Site = Get("拠点"),
                    EmploymentType = Get("雇用区分"),
                    HourlyWage = ParseInt(Get("時給"), "時給", errors),
                    CommuteAllowancePerDay = ParseInt(Get("通勤手当日額"), "通勤手当日額", errors),
                    ResidentTax = ParseInt(Get("住民税月額"), "住民税月額", errors),
                };
                if (staff.EmployeeNo == "" || staff.Name == "")
                {
                    errors.Add("スタッフ番号・氏名は必須です。");
                }
                else if (staff.EmployeeNo.Length > 8 || !staff.EmployeeNo.All(char.IsAsciiDigit))
                {
                    errors.Add($"スタッフ番号「{staff.EmployeeNo}」は1～8桁の数字で入力してください（スタッフナビゲーターのスタッフ番号）。");
                }
                else if (!seen.Add(staff.EmployeeNo))
                {
                    errors.Add($"スタッフ番号「{staff.EmployeeNo}」が重複しています。");
                }
                var birthText = birthIndex >= 0 ? Cell(row, birthIndex) : "";
                if (birthText != "")
                {
                    if (DateOnly.TryParseExact(birthText, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
                    {
                        staff.BirthDate = birth;
                    }
                    else
                    {
                        errors.Add($"生年月日「{birthText}」の形式が正しくありません。");
                    }
                }
                var dependentsText = dependentsIndex >= 0 ? Cell(row, dependentsIndex) : "";
                staff.Dependents = dependentsText == "" ? 0 : ParseInt(dependentsText, "扶養家族人数", errors);
                if (DateOnly.TryParseExact(Get("入社日"), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var hireDate))
                {
                    staff.HireDate = hireDate;
                }
                else
                {
                    errors.Add($"入社日「{Get("入社日")}」の形式が正しくありません。");
                }
                foreach (var error in errors)
                {
                    AddIssue(pending, line, ImportIssue.Error, error, staff.EmployeeNo);
                }
                if (errors.Count == 0)
                {
                    pending.Data.Staff.Add(staff);
                }
            }
            if (pending.Data.Staff.Count == 0 && pending.ErrorCount == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "取込対象の行がありません。");
            }
        }

        private static Dictionary<string, int>? MapHeader(PendingImport pending, List<string> header, IEnumerable<string> required)
        {
            var missing = required.Where(h => !header.Contains(h)).ToList();
            if (missing.Count > 0)
            {
                AddIssue(pending, 1, ImportIssue.Error, $"必要な項目がありません：{string.Join("、", missing)}");
                return null;
            }
            return required.ToDictionary(h => h, h => header.IndexOf(h));
        }

        private static TimeOnly? ParseTime(string text, string name, List<string> errors)
        {
            if (text == "")
            {
                return null;
            }
            if (TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                return time;
            }
            errors.Add($"{name}「{text}」の形式が正しくありません（例：08:30）。");
            return null;
        }

        private static int ParseInt(string text, string name, List<string> errors)
        {
            if (int.TryParse(text.Replace(",", ""), out var value) && value >= 0)
            {
                return value;
            }
            errors.Add($"{name}「{text}」は0以上の数値で入力してください。");
            return 0;
        }

        private static string Cell(string[] row, int index) => index < row.Length ? row[index].Trim() : "";

        private static string? NullIfEmpty(string text) => text == "" ? null : text;

        private static void AddIssue(PendingImport pending, int line, string level, string message, string? employeeNo = null, DateOnly? date = null) =>
            pending.Data.Issues.Add(new ImportIssue { LineNo = line, Level = level, Message = message, EmployeeNo = employeeNo, Date = date });

        /// <summary>UTF-8（BOM有無）とShift_JISを自動判別する</summary>
        private static (string Text, string Encoding) Decode(byte[] bytes)
        {
            try
            {
                var text = new UTF8Encoding(false, true).GetString(bytes);
                return (text.TrimStart('﻿'), "UTF-8");
            }
            catch (DecoderFallbackException)
            {
                return (Encoding.GetEncoding(932).GetString(bytes), "Shift_JIS");
            }
        }
    }

    public static class CsvText
    {
        public static List<string[]> Parse(string text)
        {
            var rows = new List<string[]>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. row]);
                    row.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add([.. row]);
            }
            return rows;
        }

        public static string Write(IEnumerable<IEnumerable<object?>> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
            {
                sb.AppendJoin(',', row.Select(Escape));
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>Excelで文字化けしないよう、BOM付きUTF-8で出力する（スタッフナビゲーター形式のCSVは StaffNavigatorFormat.ToShiftJis を使う）</summary>
        public static byte[] ToExcelBytes(string csv) => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)];

        private static string Escape(object? value)
        {
            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
        }
    }
}
