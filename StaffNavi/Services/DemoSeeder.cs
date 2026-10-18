using System.Text;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// デモ用ダミーデータの生成（実在の人物・企業とは関係ありません）
    /// </summary>
    public class DemoSeeder(LaborRepository repo, CsvImportService importer, LaborService labor)
    {
        public const string AdminName = "山田 太郎";
        public const string StaffUserName = "鈴木 花子";

        /// <summary>CSV取込のデモで使うサンプルの対象月</summary>
        public const int SampleYear = 2026;
        public const int SampleMonth = 9;

        private static readonly (TimeOnly Start, TimeOnly End)[] Shifts =
        [
            (new(6, 0), new(15, 0)),
            (new(8, 0), new(17, 0)),
            (new(9, 0), new(18, 0)),
            (new(13, 0), new(22, 0)),
            (new(14, 0), new(23, 0)),
            (new(7, 0), new(16, 0)),
        ];

        public static List<StaffMember> CreateStaff()
        {
            (string Name, string Kana, string Gender, string Birth, string Site, string Type, int Wage, int Commute, int Tax, int Dependents, string Hire)[] rows =
            [
                ("田中 健一", "タナカ ケンイチ", "男", "1978/05/30", "大阪南SS", "契約社員", 1350, 420, 9800, 2, "2019/04/01"),
                ("佐藤 美咲", "サトウ ミサキ", "女", "1990/11/12", "名古屋東SS", "パート", 1180, 300, 3200, 0, "2021/06/15"),
                ("鈴木 大輔", "スズキ ダイスケ", "男", "1972/02/08", "横浜西SS", "契約社員", 1400, 560, 11200, 3, "2018/10/01"),
                ("高橋 ゆかり", "タカハシ ユカリ", "女", "1985/07/21", "札幌北SS", "パート", 1150, 0, 2500, 1, "2022/02/01"),
                ("伊藤 翔", "イトウ ショウ", "男", "2003/04/02", "福岡東SS", "アルバイト", 1100, 240, 0, 0, "2024/04/10"),
                ("渡辺 直樹", "ワタナベ ナオキ", "男", "1981/09/15", "広島南SS", "契約社員", 1300, 380, 8600, 2, "2020/09/01"),
                ("山本 彩", "ヤマモト アヤ", "女", "1994/12/03", "大阪南SS", "パート", 1200, 280, 3800, 0, "2023/01/16"),
                ("中村 拓也", "ナカムラ タクヤ", "男", "1988/03/27", "名古屋東SS", "契約社員", 1320, 460, 9100, 1, "2019/07/01"),
                ("小林 優子", "コバヤシ ユウコ", "女", "1976/08/19", "横浜西SS", "パート", 1250, 320, 4100, 0, "2021/11/01"),
                ("加藤 誠", "カトウ マコト", "男", "1968/01/10", "札幌北SS", "契約社員", 1280, 500, 8200, 1, "2017/04/01"),
                ("吉田 さくら", "ヨシダ サクラ", "女", "2005/06/28", "福岡東SS", "アルバイト", 1120, 200, 0, 0, "2025/03/01"),
                ("山口 浩二", "ヤマグチ コウジ", "男", "1965/10/05", "広島南SS", "契約社員", 1310, 440, 8800, 1, "2016/08/01"),
                ("松本 恵", "マツモト メグミ", "女", "1983/04/14", "大阪南SS", "パート", 1190, 260, 3000, 2, "2022/08/01"),
                ("井上 隆", "イノウエ タカシ", "男", "1999/09/09", "名古屋東SS", "アルバイト", 1150, 220, 0, 0, "2024/10/01"),
                ("木村 涼子", "キムラ リョウコ", "女", "1979/02/23", "横浜西SS", "パート", 1260, 340, 4300, 1, "2020/05/11"),
                ("林 大樹", "ハヤシ ダイキ", "男", "1986/12/17", "札幌北SS", "契約社員", 1290, 480, 8400, 2, "2018/12/01"),
                ("清水 真由美", "シミズ マユミ", "女", "1974/06/06", "福岡東SS", "パート", 1170, 250, 2800, 0, "2023/07/01"),
                ("森 健太", "モリ ケンタ", "男", "2001/11/30", "広島南SS", "アルバイト", 1130, 230, 0, 0, "2025/06/01"),
            ];
            return rows.Select((r, i) => new StaffMember
            {
                EmployeeNo = (FirstStaffNo + i).ToString(),
                Name = r.Name,
                Kana = r.Kana,
                Gender = r.Gender,
                BirthDate = DateOnly.ParseExact(r.Birth, "yyyy/MM/dd"),
                Site = r.Site,
                EmploymentType = r.Type,
                HourlyWage = r.Wage,
                CommuteAllowancePerDay = r.Commute,
                ResidentTax = r.Tax,
                Dependents = r.Dependents,
                HireDate = DateOnly.ParseExact(r.Hire, "yyyy/MM/dd"),
            }).ToList();
        }

        /// <summary>スタッフ番号の開始番号（スタッフナビゲーターと同じく数字のみ）</summary>
        private const int FirstStaffNo = 1001;

        /// <summary>1か月分の勤怠実績（打刻漏れ・休憩不足などの要確認データを意図的に含む）</summary>
        public static List<AttendanceRecord> CreateAttendance(List<StaffMember> staffList, int year, int month)
        {
            var rng = new Random(year * 100 + month);
            var days = DateTime.DaysInMonth(year, month);
            var records = new List<AttendanceRecord>();
            var missingEnd = (staff: month % staffList.Count, day: 12);
            var shortBreak = (staff: (month + 5) % staffList.Count, day: 20);
            var missingStart = (staff: (month + 11) % staffList.Count, day: 8);

            for (var i = 0; i < staffList.Count; i++)
            {
                var staff = staffList[i];
                var shift = Shifts[i % Shifts.Length];
                var off1 = (DayOfWeek)(i * 2 % 7);
                var off2 = (DayOfWeek)((i * 2 + 3) % 7);
                for (var d = 1; d <= days; d++)
                {
                    var date = new DateOnly(year, month, d);
                    var kind = date.DayOfWeek == off1 || date.DayOfWeek == off2 ? DayKind.DayOff : DayKind.Work;
                    var roll = rng.NextDouble();
                    if (kind == DayKind.Work && roll < 0.03)
                    {
                        kind = DayKind.PaidLeave;
                    }
                    else if (kind == DayKind.Work && roll < 0.037)
                    {
                        kind = DayKind.Absence;
                    }
                    else if (kind == DayKind.DayOff && roll < 0.05)
                    {
                        kind = DayKind.HolidayWork;
                    }

                    var forced = (i, d) == missingEnd || (i, d) == shortBreak || (i, d) == missingStart;
                    if (forced)
                    {
                        kind = DayKind.Work;
                    }

                    var record = new AttendanceRecord
                    {
                        EmployeeNo = staff.EmployeeNo,
                        Name = staff.Name,
                        Site = staff.Site,
                        Date = date,
                        Kind = kind,
                    };
                    if (kind.IsWorking())
                    {
                        record.Start = shift.Start;
                        record.End = rng.NextDouble() < 0.25 ? shift.End.AddMinutes(30 * rng.Next(1, 5)) : shift.End;
                        record.BreakMinutes = 60;
                    }
                    if ((i, d) == missingEnd)
                    {
                        record.End = null;
                    }
                    if ((i, d) == missingStart)
                    {
                        record.Start = null;
                    }
                    if ((i, d) == shortBreak)
                    {
                        record.End = shift.End.AddMinutes(60);
                        record.BreakMinutes = 30;
                    }
                    records.Add(record);
                }
            }
            return records;
        }

        public static string AttendanceCsv(IEnumerable<AttendanceRecord> records) =>
            CsvText.Write(new[] { CsvImportService.AttendanceHeaders.Cast<object?>() }.Concat(
                records.OrderBy(r => r.EmployeeNo).ThenBy(r => r.Date).Select(r => new object?[]
                {
                    r.EmployeeNo, r.Name, r.Site, r.Date.ToString("yyyy/MM/dd"), r.Kind.Label(),
                    Fmt.Time(r.Start), Fmt.Time(r.End), r.Kind.IsWorking() ? r.BreakMinutes : "", r.Note,
                })));

        public static string StaffCsv(IEnumerable<StaffMember> staff) =>
            CsvText.Write(new[] { CsvImportService.StaffHeaders.Cast<object?>() }.Concat(
                staff.Select(s => new object?[]
                {
                    s.EmployeeNo, s.Name, s.Kana, s.Gender, s.BirthDate?.ToString("yyyy/MM/dd"), s.Site, s.EmploymentType,
                    s.HireDate.ToString("yyyy/MM/dd"), s.HourlyWage, s.CommuteAllowancePerDay, s.ResidentTax, s.Dependents,
                })));

        /// <summary>スタッフナビゲーターの出力を想定し、Shift_JISでCSVを作成する</summary>
        public static byte[] ToShiftJis(string csv) => Encoding.GetEncoding(932).GetBytes(csv);

        public static byte[] SampleAttendanceCsv() =>
            ToShiftJis(AttendanceCsv(CreateAttendance(CreateStaff(), SampleYear, SampleMonth)));

        public static string SampleAttendanceFileName => $"勤怠実績_{SampleYear}{SampleMonth:00}.csv";

        public void EnsureSeeded()
        {
            if (!repo.IsInitialized)
            {
                Seed();
            }
        }

        public void Reset(string user)
        {
            repo.Clear();
            Seed();
            repo.AddLog(user, "設定", "デモデータを初期状態に戻しました");
        }

        private void Seed()
        {
            repo.SaveSettings(new LaborSettings());
            var staff = CreateStaff();

            Import("スタッフ情報_20260805.csv", ToShiftJis(StaffCsv(staff)), At(2026, 8, 5, 9, 5));
            Import("勤怠実績_202607.csv", ToShiftJis(AttendanceCsv(CreateAttendance(staff, 2026, 7))), At(2026, 8, 5, 9, 12));
            Import("勤怠実績_202608.csv", ToShiftJis(AttendanceCsv(CreateAttendance(staff, 2026, 8))), At(2026, 9, 4, 10, 3));

            // 7月分：要確認データはすべて補正済み
            var july = labor.GetMonth("2026-07")!;
            var minute = 0;
            foreach (var (staffMonth, day) in Alerts(july))
            {
                AddSeedCorrection(july, staffMonth, day, At(2026, 8, 6, 14, minute += 7));
            }

            // 8月分：一部補正済み、1件は補正後に取消
            var august = labor.GetMonth("2026-08")!;
            var alerts = Alerts(august).ToList();
            if (alerts.Count > 0)
            {
                AddSeedCorrection(august, alerts[0].Staff, alerts[0].Day, At(2026, 9, 7, 11, 20));
            }
            var target = august.Staff[3].Days.First(d => d.Effective?.Kind == DayKind.Work && !d.IsCorrected && d.Warnings.Count == 0);
            var values = target.Effective! with { End = target.Effective!.End?.AddMinutes(30) };
            var wrong = AddCorrection(august, august.Staff[3], target, values,
                "店舗責任者より退勤時刻の申告あり", "店舗日報（8月分）", StaffUserName, At(2026, 9, 7, 11, 35));
            const string revokeReason = "日報を再確認した結果、取込データの退勤時刻が正しかったため";
            repo.RevokeCorrection(wrong.Id, AdminName, revokeReason, At(2026, 9, 8, 9, 40));
            repo.AddLog(AdminName, "補正取消", $"{wrong.StaffName}（{wrong.EmployeeNo}）{wrong.Date:yyyy/MM/dd} の補正を取り消しました（理由：{revokeReason}）", At(2026, 9, 8, 9, 40));

            repo.AddLog(AdminName, "資料出力", "勤務表（出勤簿）を出力しました（2026年7月、全拠点、18名）", At(2026, 8, 6, 16, 2));
            repo.AddLog(AdminName, "資料出力", "賃金台帳を出力しました（2026年、全拠点、18名）", At(2026, 8, 6, 16, 5));
        }

        private void Import(string fileName, byte[] bytes, DateTimeOffset at)
        {
            var pending = importer.Parse(fileName, bytes, AdminName);
            importer.Commit(pending, AdminName, at);
        }

        private static IEnumerable<(StaffMonth Staff, DayEntry Day)> Alerts(MonthView view) =>
            view.Staff.SelectMany(s => s.Days.Where(d => d.Warnings.Count > 0).Select(d => (s, d)));

        private void AddSeedCorrection(MonthView view, StaffMonth staffMonth, DayEntry day, DateTimeOffset at)
        {
            var before = day.Effective!;
            var shift = Shifts[(int.Parse(staffMonth.Staff.EmployeeNo) - FirstStaffNo) % Shifts.Length];
            AttendanceValues after;
            string reason;
            string evidence;
            if (before.End == null)
            {
                after = before with { End = shift.End };
                reason = "退勤打刻漏れ。店舗日報および本人申告により退勤時刻を確認";
                evidence = "店舗日報・本人申告書";
            }
            else if (before.Start == null)
            {
                after = before with { Start = shift.Start };
                reason = "出勤打刻漏れ。シフト表および店舗責任者の確認により出勤時刻を確認";
                evidence = "シフト表・店舗責任者確認";
            }
            else
            {
                after = before with { BreakMinutes = 60 };
                reason = "休憩の取得実績を店舗責任者に確認。勤怠端末の休憩入力漏れのため修正";
                evidence = "店舗責任者ヒアリング記録";
            }
            AddCorrection(view, staffMonth, day, after, reason, evidence, StaffUserName, at);
        }

        private Correction AddCorrection(MonthView view, StaffMonth staffMonth, DayEntry day, AttendanceValues after,
            string reason, string evidence, string user, DateTimeOffset at)
        {
            var correction = new Correction
            {
                Id = $"C{at:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                BatchId = view.Batch.Id,
                Month = view.Month,
                EmployeeNo = staffMonth.Staff.EmployeeNo,
                StaffName = staffMonth.Staff.Name,
                Date = day.Date,
                Before = day.Effective,
                After = after,
                Reason = reason,
                Evidence = evidence,
                CorrectedBy = user,
                CorrectedAt = at,
            };
            repo.AddCorrection(correction);
            repo.AddLog(user, "補正", $"{correction.StaffName}（{correction.EmployeeNo}）{correction.Date:yyyy/MM/dd}：" +
                $"{correction.Before?.Describe()} → {after.Describe()}（理由：{reason}）", at);
            return correction;
        }

        private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
            new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));
    }
}
