using System.Globalization;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    public class DayEntry
    {
        public DateOnly Date { get; set; }
        public AttendanceRecord? Original { get; set; }
        public AttendanceValues? Effective { get; set; }
        public Correction? Correction { get; set; }
        public List<Correction> History { get; set; } = [];
        public DayCalc Calc { get; set; } = DayCalc.Empty;
        public List<string> Warnings { get; set; } = [];

        public bool IsCorrected => Correction != null;
    }

    public class StaffMonth
    {
        public StaffMember Staff { get; set; } = new();
        public List<DayEntry> Days { get; set; } = [];
        public MonthSummary Summary { get; set; } = new();
        public WageResult Wage { get; set; } = new();
    }

    public class MonthView
    {
        public string Month { get; set; } = "";
        public ImportBatch Batch { get; set; } = new();
        public LaborSettings Settings { get; set; } = new();
        public List<StaffMonth> Staff { get; set; } = [];

        public int CorrectionCount => Staff.Sum(s => s.Summary.CorrectionCount);
        public int WarningCount => Staff.Sum(s => s.Summary.WarningCount);
        public DateOnly FirstDay => DateOnly.ParseExact(Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

        /// <summary>給与締日（末締）</summary>
        public DateOnly CloseDate => LastDay;

        /// <summary>給与支払日（翌月の設定日）</summary>
        public DateOnly PayDate
        {
            get
            {
                var next = FirstDay.AddMonths(1);
                return new DateOnly(next.Year, next.Month, Math.Min(Settings.PayDayOfMonth, DateTime.DaysInMonth(next.Year, next.Month)));
            }
        }
    }

    /// <summary>
    /// 取込データ（原本）に補正内容を重ねて、勤務表・賃金台帳用のデータを組み立てる
    /// </summary>
    public class LaborService(LaborRepository repo)
    {
        /// <summary>月ごとの最新の勤怠取込（新しい月順）</summary>
        public List<ImportBatch> GetEffectiveBatches() =>
            repo.GetBatches()
                .Where(b => b.Kind == ImportKind.Attendance && b.TargetMonth != null)
                .GroupBy(b => b.TargetMonth)
                .Select(g => g.OrderByDescending(b => b.ImportedAt).First())
                .OrderByDescending(b => b.TargetMonth)
                .ToList();

        public List<string> GetMonths() => GetEffectiveBatches().Select(b => b.TargetMonth!).ToList();

        public List<int> GetYears() => GetMonths().Select(m => int.Parse(m[..4])).Distinct().OrderByDescending(y => y).ToList();

        public MonthView? GetMonth(string? month)
        {
            var batches = GetEffectiveBatches();
            var batch = string.IsNullOrEmpty(month) ? batches.FirstOrDefault() : batches.FirstOrDefault(b => b.TargetMonth == month);
            return batch == null ? null : Build(batch);
        }

        public MonthView Build(ImportBatch batch)
        {
            var settings = repo.GetSettings();
            var data = repo.GetBatchData(batch.Id);
            var staffMaster = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            var corrections = repo.GetCorrections()
                .Where(c => c.BatchId == batch.Id)
                .OrderBy(c => c.CorrectedAt)
                .ToLookup(c => (c.EmployeeNo, c.Date));

            var view = new MonthView { Month = batch.TargetMonth!, Batch = batch, Settings = settings };
            foreach (var group in data.Records.GroupBy(r => r.EmployeeNo).OrderBy(g => g.Key))
            {
                var first = group.First();
                var staff = staffMaster.GetValueOrDefault(group.Key) ?? new StaffMember
                {
                    EmployeeNo = group.Key,
                    Name = first.Name,
                    Site = first.Site,
                };
                var records = group.ToDictionary(r => r.Date);
                var staffMonth = new StaffMonth { Staff = staff };
                for (var date = view.FirstDay; date <= view.LastDay; date = date.AddDays(1))
                {
                    var original = records.GetValueOrDefault(date);
                    var history = corrections[(group.Key, date)].ToList();
                    var active = history.LastOrDefault(c => !c.Revoked);
                    var effective = active != null ? active.After : original?.Values;
                    var calc = LaborCalculator.Calculate(effective, settings);
                    staffMonth.Days.Add(new DayEntry
                    {
                        Date = date,
                        Original = original,
                        Effective = effective,
                        Correction = active,
                        History = history,
                        Calc = calc,
                        Warnings = LaborCalculator.Validate(effective, calc),
                    });
                }
                staffMonth.Summary = Summarize(staffMonth.Days, settings);
                staffMonth.Wage = LaborCalculator.CalculateWage(staff, staffMonth.Summary, settings, view.FirstDay);
                view.Staff.Add(staffMonth);
            }
            return view;
        }

        /// <summary>指定年の賃金台帳（月ごと）</summary>
        public List<(string Month, MonthView View, StaffMonth? Staff)> GetLedger(int year, string employeeNo) =>
            GetEffectiveBatches()
                .Where(b => b.TargetMonth!.StartsWith($"{year}-"))
                .OrderBy(b => b.TargetMonth)
                .Select(b => Build(b))
                .Select(v => (v.Month, v, v.Staff.FirstOrDefault(s => s.Staff.EmployeeNo == employeeNo)))
                .ToList();

        public string? AddCorrection(string month, string employeeNo, DateOnly date, AttendanceValues after,
            string reason, string? evidence, string user)
        {
            var view = GetMonth(month);
            var staffMonth = view?.Staff.FirstOrDefault(s => s.Staff.EmployeeNo == employeeNo);
            var day = staffMonth?.Days.FirstOrDefault(d => d.Date == date);
            if (view == null || staffMonth == null || day == null)
            {
                return "対象の勤務データが見つかりません。";
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "補正理由を入力してください。";
            }
            if (after.Kind.IsWorking())
            {
                if (after.Start == null || after.End == null)
                {
                    return "出勤・休日出勤の場合は、出勤時刻と退勤時刻を入力してください。";
                }
                if (after.BreakMinutes is < 0 or > 600)
                {
                    return "休憩時間の値が正しくありません。";
                }
            }
            else
            {
                after = new AttendanceValues(after.Kind, null, null, 0);
            }
            if (after == day.Effective)
            {
                return "変更内容がありません。";
            }

            var now = Clock.Now;
            var correction = new Correction
            {
                Id = $"C{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                BatchId = view.Batch.Id,
                Month = view.Month,
                EmployeeNo = employeeNo,
                StaffName = staffMonth.Staff.Name,
                Date = date,
                Before = day.Effective,
                After = after,
                Reason = reason.Trim(),
                Evidence = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim(),
                CorrectedBy = user,
                CorrectedAt = now,
            };
            repo.AddCorrection(correction);
            repo.AddLog(user, "補正", $"{staffMonth.Staff.Name}（{employeeNo}）{date:yyyy/MM/dd}：" +
                $"{correction.Before?.Describe() ?? "データなし"} → {after.Describe()}（理由：{correction.Reason}）", now);
            return null;
        }

        public string? RevokeCorrection(string id, string reason, string user)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "取消理由を入力してください。";
            }
            var target = repo.GetCorrections().FirstOrDefault(c => c.Id == id);
            if (target == null || !repo.RevokeCorrection(id, user, reason.Trim(), Clock.Now))
            {
                return "対象の補正が見つからないか、すでに取消済みです。";
            }
            repo.AddLog(user, "補正取消", $"{target.StaffName}（{target.EmployeeNo}）{target.Date:yyyy/MM/dd} の補正を取り消しました（理由：{reason.Trim()}）");
            return null;
        }

        private static MonthSummary Summarize(List<DayEntry> days, LaborSettings settings)
        {
            var summary = new MonthSummary();
            foreach (var day in days)
            {
                switch (day.Effective?.Kind)
                {
                    case DayKind.Work: summary.WorkDays++; break;
                    case DayKind.HolidayWork: summary.HolidayWorkDays++; break;
                    case DayKind.PaidLeave: summary.PaidLeaveDays++; break;
                    case DayKind.Absence: summary.AbsenceDays++; break;
                    case DayKind.DayOff: summary.DayOffDays++; break;
                }
                summary.WorkMinutes += day.Calc.WorkMinutes;
                summary.BasicMinutes += day.Calc.BasicMinutes;
                summary.OvertimeMinutes += day.Calc.OvertimeMinutes;
                summary.NightMinutes += day.Calc.NightMinutes;
                summary.NightOvertimeMinutes += day.Calc.NightOvertimeMinutes;
                summary.HolidayMinutes += day.Calc.HolidayMinutes;
                summary.WarningCount += day.Warnings.Count;
                summary.CorrectionCount += day.IsCorrected ? 1 : 0;
            }
            summary.PaidLeaveMinutes = summary.PaidLeaveDays * settings.PaidLeaveMinutes;
            return summary;
        }
    }
}
