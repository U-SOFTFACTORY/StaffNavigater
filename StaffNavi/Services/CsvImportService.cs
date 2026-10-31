using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// スタッフナビゲーターから手動出力したCSVの読込・検証・取込。
    /// 形式は先方から提供された実データ（タイムシートA勤.csv／staff 全データ.csv／26.10支給給与計算書.csv）に合わせている。
    /// </summary>
    public class CsvImportService(LaborRepository repo)
    {
        /// <summary>タイムシートCSV（受注ごとの日別実績）</summary>
        public static readonly string[] TimesheetHeaders = ["スタッフ番号", "受注番号", "就業日", "就業データ番号", "出欠状況", "始業時刻", "終業時刻", "休憩時間"];

        /// <summary>スタッフ全データCSV（202項目）のうち取り込む項目</summary>
        public static readonly string[] StaffHeaders =
        [
            "ｽﾀｯﾌ番号", "ｽﾀｯﾌ区分", "氏名(ｶﾅ)", "氏名(漢字)", "性別", "生年月日", "入社日", "退職", "退職日", "雇用形態",
            "厚生年金加入状況", "厚生年金等級", "健康保険加入状況", "健康保険等級", "介護保険", "雇用保険加入状況",
            "税区分", "扶養家族人数", "住民税(7月以降)", "１日あたり有給時間",
        ];

        private static readonly string[] RequiredStaffHeaders = ["ｽﾀｯﾌ番号", "氏名(漢字)"];

        private static readonly string[] DateFormats = ["yyyy/M/d", "yyyy-M-d", "yyyyMMdd"];

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

            var header = rows[0].Select(h => h.Trim()).ToList();
            if (header.Contains("就業日") && header.Contains("受注番号"))
            {
                pending.Kind = ImportKind.Attendance;
                ParseTimesheet(pending, header, rows);
            }
            else if (header.Contains("総支給額") && header.Contains("差引支給額"))
            {
                pending.Kind = ImportKind.Payroll;
                ParsePayroll(pending, header, rows);
            }
            else if (header.Contains("ｽﾀｯﾌ番号") && header.Contains("氏名(漢字)"))
            {
                pending.Kind = ImportKind.Staff;
                ParseStaff(pending, header, rows);
            }
            else
            {
                AddIssue(pending, 1, ImportIssue.Error, "CSVの形式を判別できません。スタッフナビゲーターから出力したタイムシート／スタッフ全データ／給与計算書のCSVを選択してください（1行目に項目名が必要です）。");
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
                OrderNos = pending.OrderNos,
                RowCount = pending.RowCount,
                WarningCount = pending.WarningCount,
            };
            repo.AddBatch(batch, pending.Data);
            var target = batch.TargetMonth == null ? "" :
                batch.Kind == ImportKind.Payroll ? $"支給月：{Fmt.Month(batch.TargetMonth)}、" : $"就業月：{Fmt.Month(batch.TargetMonth)}、受注{batch.OrderNos.Count}件、";
            repo.AddLog(user, "CSV取込", $"{batch.Kind.Label()}CSV「{batch.FileName}」を取り込みました（{target}{batch.RowCount}件、警告{batch.WarningCount}件）", now);
            return batch;
        }

        private void ParseTimesheet(PendingImport pending, List<string> header, List<string[]> rows)
        {
            var index = MapHeader(pending, header, TimesheetHeaders);
            if (index == null)
            {
                return;
            }
            var staff = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            var orders = repo.GetOrders().ToDictionary(o => o.OrderNo);
            if (staff.Count == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "スタッフ情報が未登録です。先にスタッフ全データCSVを取り込んでください。");
            }
            var seen = new HashSet<(string, string, DateOnly)>();
            var unknownOrders = new HashSet<string>();

            for (var i = 1; i < rows.Count; i++)
            {
                var line = i + 1;
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                string Get(string name) => Cell(row, index[name]);

                var record = new AttendanceRecord
                {
                    LineNo = line,
                    EmployeeNo = TrimNo(Get("スタッフ番号")),
                    OrderNo = Get("受注番号"),
                    Status = Get("出欠状況"),
                };
                var errors = new List<string>();
                if (record.EmployeeNo == "")
                {
                    errors.Add("スタッフ番号が未入力です。");
                }
                else if (staff.Count > 0 && !staff.ContainsKey(record.EmployeeNo))
                {
                    errors.Add($"スタッフ番号「{record.EmployeeNo}」がスタッフ情報に存在しません。");
                }
                if (!orders.TryGetValue(record.OrderNo, out var order))
                {
                    // 未登録の受注は受注ごとに1回だけ表示する（2行目以降は行をエラー扱いにして取り込まない）
                    errors.Add(unknownOrders.Add(record.OrderNo) ? $"受注番号「{record.OrderNo}」が受注一覧に登録されていません（設定画面の受注一覧に追加してください）。" : "");
                }
                if (DateOnly.TryParseExact(Get("就業日"), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    record.Date = date;
                }
                else
                {
                    errors.Add($"就業日「{Get("就業日")}」の形式が正しくありません。");
                }
                record.DataNo = int.TryParse(Get("就業データ番号"), out var dataNo) ? dataNo : 1;
                if (DayKindExtensions.TryParseStatus(record.Status, out var kind))
                {
                    record.Kind = kind;
                }
                else
                {
                    errors.Add($"出欠状況「{record.Status}」は使用できません（0～8）。");
                }
                record.Start = ParseClock(Get("始業時刻"), "始業時刻", errors);
                record.End = ParseClock(Get("終業時刻"), "終業時刻", errors);
                // 終業が始業以前（例：22:00～8:00）の場合は翌日の時刻とし、勤務表と同じく 32:00 のように持つ
                if (record.Start != null && record.End != null && record.End <= record.Start && record.End < 24 * 60)
                {
                    record.End += 24 * 60;
                }
                var breakText = Get("休憩時間");
                if (breakText == "")
                {
                    record.BreakMinutes = 0;
                }
                else if (int.TryParse(breakText, out var breakMinutes) && breakMinutes >= 0)
                {
                    record.BreakMinutes = breakMinutes;
                }
                else if (breakText.Contains(':') && Fmt.TryParseClock(breakText, out var breakClock))
                {
                    record.BreakMinutes = breakClock;
                }
                else
                {
                    errors.Add($"休憩時間「{breakText}」は分（例：60）で入力してください。");
                }
                if (errors.Count == 0 && !seen.Add((record.EmployeeNo, record.OrderNo, record.Date)))
                {
                    errors.Add($"同じスタッフ・受注・就業日の行が重複しています（{record.Date:yyyy/MM/dd}）。1日に複数の就業データ（就業データ番号2以降）はデモでは未対応です。");
                }

                foreach (var error in errors.Where(e => e != ""))
                {
                    AddIssue(pending, line, ImportIssue.Error, error, record.EmployeeNo, record.Date == default ? null : record.Date);
                }
                if (errors.Count > 0)
                {
                    continue;
                }
                var paidHours = staff.GetValueOrDefault(record.EmployeeNo)?.PaidLeaveHoursPerDay ?? 8;
                var calc = LaborCalculator.Calculate(record.Values, order!, paidHours);
                foreach (var warning in LaborCalculator.Validate(record.Values, calc))
                {
                    AddIssue(pending, line, ImportIssue.Warning, warning, record.EmployeeNo, record.Date);
                }
                pending.Data.Records.Add(record);
            }

            pending.OrderNos = pending.Data.Records.Select(r => r.OrderNo).Distinct().Order().ToList();
            var months = pending.Data.Records.Select(r => r.Date.ToString("yyyy-MM")).Distinct().ToList();
            if (months.Count > 1)
            {
                AddIssue(pending, 0, ImportIssue.Error, $"複数の月のデータが含まれています（{string.Join("、", months.Select(Fmt.Month))}）。1ファイル1か月分（給与締の期間）で出力してください。");
            }
            else if (months.Count == 1)
            {
                pending.TargetMonth = months[0];
                var existing = repo.GetBatches()
                    .Where(b => b.Kind == ImportKind.Attendance && b.TargetMonth == months[0] && b.OrderNos.Intersect(pending.OrderNos).Any())
                    .OrderByDescending(b => b.ImportedAt)
                    .FirstOrDefault();
                if (existing != null)
                {
                    var same = existing.OrderNos.Intersect(pending.OrderNos).ToList();
                    pending.Notices.Add($"{Fmt.Month(months[0])}分の受注（{string.Join("・", same)}）は {Fmt.DateTime(existing.ImportedAt)} に取込済みです。" +
                        "取込を確定すると、これらの受注は今回のデータを最新として使用します（以前の取込データと補正履歴は削除されず保持されます）。");
                }
                else if (repo.GetBatches().Any(b => b.Kind == ImportKind.Attendance && b.TargetMonth == months[0]))
                {
                    pending.Notices.Add($"{Fmt.Month(months[0])}分のほかの受注のタイムシートと合わせて、勤務表を作成します（掛け持ちのスタッフは受注ごとに勤務表を分けて表示します）。");
                }
            }
            else if (pending.ErrorCount == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "取込対象の行がありません。");
            }
        }

        private static void ParseStaff(PendingImport pending, List<string> header, List<string[]> rows)
        {
            var index = MapHeader(pending, header, RequiredStaffHeaders);
            if (index == null)
            {
                return;
            }
            var missing = StaffHeaders.Where(h => !header.Contains(h)).ToList();
            if (missing.Count > 0)
            {
                pending.Notices.Add($"次の項目がないため、空欄として取り込みます：{string.Join("、", missing)}");
            }
            var seen = new HashSet<string>();
            for (var i = 1; i < rows.Count; i++)
            {
                var line = i + 1;
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                string Get(string name) => header.IndexOf(name) is var x and >= 0 ? Cell(row, x) : "";
                var errors = new List<string>();
                DateOnly? Date(string name)
                {
                    var text = Get(name);
                    if (text == "")
                    {
                        return null;
                    }
                    if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    {
                        return d;
                    }
                    errors.Add($"{name}「{text}」の形式が正しくありません。");
                    return null;
                }
                int Int(string name) => int.TryParse(Get(name).Replace(",", ""), out var v) ? v : 0;

                var staff = new StaffMember
                {
                    EmployeeNo = TrimNo(Get("ｽﾀｯﾌ番号")),
                    Name = Get("氏名(漢字)"),
                    Kana = Get("氏名(ｶﾅ)"),
                    Gender = Get("性別"),
                    Category = Get("ｽﾀｯﾌ区分"),
                    EmploymentForm = Get("雇用形態"),
                    BirthDate = Date("生年月日"),
                    HireDate = Date("入社日"),
                    Retired = Get("退職") == "退職",
                    RetireDate = Date("退職日"),
                    PensionInsured = Get("厚生年金加入状況") == "加入済",
                    PensionGrade = Int("厚生年金等級"),
                    HealthInsured = Get("健康保険加入状況") == "加入済",
                    HealthGrade = Int("健康保険等級"),
                    CareInsured = Get("介護保険") == "対象",
                    EmploymentInsured = Get("雇用保険加入状況") == "加入済",
                    TaxCategory = Get("税区分"),
                    Dependents = Int("扶養家族人数"),
                    ResidentTax = Int("住民税(7月以降)"),
                    PaidLeaveHoursPerDay = decimal.TryParse(Get("１日あたり有給時間"), out var paid) && paid > 0 ? paid : 8,
                };
                if (staff.EmployeeNo != "" && staff.Name == "")
                {
                    // 実データにも氏名のない行（登録途中のデータなど）があるため、警告として読み飛ばす
                    AddIssue(pending, line, ImportIssue.Warning, "氏名(漢字)が空欄のため、この行は取り込みません。", staff.EmployeeNo);
                    continue;
                }
                if (staff.EmployeeNo == "")
                {
                    errors.Add("ｽﾀｯﾌ番号は必須です。");
                }
                else if (staff.EmployeeNo.Length > 8 || !staff.EmployeeNo.All(char.IsAsciiDigit))
                {
                    errors.Add($"ｽﾀｯﾌ番号「{staff.EmployeeNo}」は1～8桁の数字で入力してください。");
                }
                else if (!seen.Add(staff.EmployeeNo))
                {
                    errors.Add($"ｽﾀｯﾌ番号「{staff.EmployeeNo}」が重複しています。");
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
            else
            {
                var active = pending.Data.Staff.Count(s => !s.Retired);
                pending.Notices.Add($"在職 {active}名・退職 {pending.Data.Staff.Count - active}名。取込を確定すると、スタッフ情報をこのファイルの内容に置き換えます。");
            }
        }

        private void ParsePayroll(PendingImport pending, List<string> header, List<string[]> rows)
        {
            var index = MapHeader(pending, header, PayrollFormat.Required);
            if (index == null)
            {
                return;
            }
            var staff = repo.GetStaff().Select(s => s.EmployeeNo).ToHashSet();
            var seen = new HashSet<string>();
            for (var i = 1; i < rows.Count; i++)
            {
                var line = i + 1;
                var row = rows[i];
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }
                var values = new Dictionary<string, string>();
                for (var c = 0; c < header.Count; c++)
                {
                    if (header[c] != "")
                    {
                        values[header[c]] = Cell(row, c);
                    }
                }
                var record = new PayrollRecord
                {
                    LineNo = line,
                    EmployeeNo = TrimNo(values["ｽﾀｯﾌ番号"]),
                    Name = values["ｽﾀｯﾌ氏名"],
                    Values = values,
                };
                var errors = new List<string>();
                if (DateOnly.TryParseExact(values["支払日"], DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var payDate))
                {
                    record.PayDate = payDate;
                }
                else
                {
                    errors.Add($"支払日「{values["支払日"]}」の形式が正しくありません。");
                }
                if (record.EmployeeNo == "")
                {
                    errors.Add("ｽﾀｯﾌ番号が未入力です。");
                }
                else if (!seen.Add(record.EmployeeNo))
                {
                    errors.Add($"ｽﾀｯﾌ番号「{record.EmployeeNo}」が重複しています。");
                }
                foreach (var error in errors)
                {
                    AddIssue(pending, line, ImportIssue.Error, error, record.EmployeeNo);
                }
                if (errors.Count > 0)
                {
                    continue;
                }
                if (staff.Count > 0 && !staff.Contains(record.EmployeeNo))
                {
                    AddIssue(pending, line, ImportIssue.Warning, "スタッフ情報に存在しないスタッフです（賃金台帳の性別・生年月日などは空欄になります）", record.EmployeeNo);
                }
                var ledger = LedgerMonth.FromPayroll(record, null, pending.FileName);
                if (ledger.GrossPay - ledger.TotalDeduction != ledger.NetPay)
                {
                    AddIssue(pending, line, ImportIssue.Warning, $"総支給額－総控除額（{Fmt.Yen(ledger.GrossPay - ledger.TotalDeduction)}）と差引支給額（{Fmt.Yen(ledger.NetPay)}）が一致しません", record.EmployeeNo);
                }
                pending.Data.Payroll.Add(record);
            }

            var months = pending.Data.Payroll.Select(r => r.PayDate.ToString("yyyy-MM")).Distinct().ToList();
            if (months.Count > 1)
            {
                AddIssue(pending, 0, ImportIssue.Error, $"複数の支給月のデータが含まれています（{string.Join("、", months.Select(Fmt.Month))}）。1ファイル1か月分で出力してください。");
            }
            else if (months.Count == 1)
            {
                pending.TargetMonth = months[0];
                var existing = repo.GetBatches().Where(b => b.Kind == ImportKind.Payroll && b.TargetMonth == months[0]).OrderByDescending(b => b.ImportedAt).FirstOrDefault();
                if (existing != null)
                {
                    pending.Notices.Add($"{Fmt.Month(months[0])}支給分は {Fmt.DateTime(existing.ImportedAt)} に取込済みです。取込を確定すると、今回のデータを最新として使用します。");
                }
                pending.Notices.Add($"{Fmt.Month(months[0])}支給分（対象期間 {PeriodLabel(months[0])}）の賃金台帳として使用します。");
            }
            else if (pending.ErrorCount == 0)
            {
                AddIssue(pending, 0, ImportIssue.Error, "取込対象の行がありません。");
            }
        }

        private static string PeriodLabel(string payMonth)
        {
            var first = DateOnly.ParseExact(payMonth + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(-1);
            return $"{first:yyyy/M/d}～{first.AddMonths(1).AddDays(-1):M/d}";
        }

        private static Dictionary<string, int>? MapHeader(PendingImport pending, List<string> header, IEnumerable<string> required)
        {
            var list = required.ToList();
            var missing = list.Where(h => !header.Contains(h)).ToList();
            if (missing.Count > 0)
            {
                AddIssue(pending, 1, ImportIssue.Error, $"必要な項目がありません：{string.Join("、", missing)}");
                return null;
            }
            return list.ToDictionary(h => h, h => header.IndexOf(h));
        }

        private static int? ParseClock(string text, string name, List<string> errors)
        {
            if (text == "")
            {
                return null;
            }
            if (Fmt.TryParseClock(text, out var minutes))
            {
                return minutes;
            }
            errors.Add($"{name}「{text}」の形式が正しくありません（例：6:30、32:00）。");
            return null;
        }

        /// <summary>先頭の0を除いたスタッフ番号（勤務表では 02003105 のように8桁で表示される）</summary>
        private static string TrimNo(string text)
        {
            var trimmed = text.Trim().TrimStart('0');
            return trimmed == "" && text.Trim() != "" ? "0" : trimmed;
        }

        private static string Cell(string[] row, int index) => index < row.Length ? row[index].Trim() : "";

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
