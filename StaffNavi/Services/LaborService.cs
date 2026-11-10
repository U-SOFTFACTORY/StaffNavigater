using System.Globalization;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    public class DayEntry
    {
        public DateOnly Date { get; set; }
        public string OrderNo { get; set; } = "";
        public AttendanceRecord? Original { get; set; }
        public AttendanceValues? Effective { get; set; }
        public Correction? Correction { get; set; }
        public List<Correction> History { get; set; } = [];
        public DayCalc Calc { get; set; } = DayCalc.Empty;
        public List<string> Warnings { get; set; } = [];

        public bool IsCorrected => Correction != null;

        public bool HasData => Effective != null && Effective.Kind != DayKind.NoWork;

        /// <summary>勤務表の「所定」欄（休日出勤のときは休日）</summary>
        public string Calendar => Effective?.Kind == DayKind.HolidayWork ? "休日" : "平日";
    }

    /// <summary>スタッフ×受注ごとの勤務表（スタッフナビゲーターの「勤務表（実績）」1枚分）</summary>
    public class OrderSheet
    {
        public WorkOrder Order { get; set; } = new();
        public string BatchId { get; set; } = "";
        public string FileName { get; set; } = "";
        public DateTimeOffset ImportedAt { get; set; }
        public List<DayEntry> Days { get; set; } = [];
        public MonthSummary Summary { get; set; } = new();
    }

    public class StaffMonth
    {
        public StaffMember Staff { get; set; } = new();
        public List<OrderSheet> Sheets { get; set; } = [];
        public MonthSummary Summary { get; set; } = new();

        public IEnumerable<DayEntry> AllDays => Sheets.SelectMany(s => s.Days);

        public IEnumerable<string> Workplaces => Sheets.Select(s => s.Order.Workplace).Distinct();

        /// <summary>主な就業先（労働時間が最も長い受注）</summary>
        public string MainWorkplace => Sheets.OrderByDescending(s => s.Summary.TotalMinutes).Select(s => s.Order.Workplace).FirstOrDefault() ?? "";

        public bool IsMultiSite => Sheets.Select(s => s.Order.Workplace).Distinct().Count() > 1;
    }

    public class MonthView
    {
        public string Month { get; set; } = "";
        public List<ImportBatch> Batches { get; set; } = [];
        public LaborSettings Settings { get; set; } = new();
        public List<StaffMonth> Staff { get; set; } = [];

        public int CorrectionCount => Staff.Sum(s => s.Summary.CorrectionCount);
        public int WarningCount => Staff.Sum(s => s.Summary.WarningCount);
        public DateOnly FirstDay => DateOnly.ParseExact(Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
        public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

        /// <summary>給与締日（末締）</summary>
        public DateOnly CloseDate => LastDay;

        /// <summary>給与支払日（翌月の設定日。土日の場合は直前の金曜日）</summary>
        public DateOnly PayDate => LaborService.PayDateFor(FirstDay, Settings);

        public string SourceLabel => string.Join("、", Batches.Select(b => b.FileName));

        public IEnumerable<OrderSheet> Sheets => Staff.SelectMany(s => s.Sheets);
    }

    /// <summary>
    /// 取込データ（原本）に補正内容を重ねて、勤務表・賃金台帳用のデータを組み立てる
    /// </summary>
    public class LaborService(LaborRepository repo)
    {
        /// <summary>就業月の翌月の支払日（土日は直前の平日）</summary>
        public static DateOnly PayDateFor(DateOnly workMonth, LaborSettings settings)
        {
            var next = new DateOnly(workMonth.Year, workMonth.Month, 1).AddMonths(1);
            var date = new DateOnly(next.Year, next.Month, Math.Min(settings.PayDayOfMonth, DateTime.DaysInMonth(next.Year, next.Month)));
            while (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                date = date.AddDays(-1);
            }
            return date;
        }

        public List<ImportBatch> GetAttendanceBatches(string month) =>
            repo.GetBatches().Where(b => b.Kind == ImportKind.Attendance && b.TargetMonth == month).OrderBy(b => b.ImportedAt).ToList();

        /// <summary>受注ごとに最新のタイムシート取込を使う（受注番号 → 取込）</summary>
        public Dictionary<string, ImportBatch> GetEffectiveOrders(string month)
        {
            var result = new Dictionary<string, ImportBatch>();
            foreach (var batch in GetAttendanceBatches(month))
            {
                foreach (var orderNo in batch.OrderNos)
                {
                    result[orderNo] = batch;
                }
            }
            return result;
        }

        /// <summary>現在使われている取込のID</summary>
        public HashSet<string> GetEffectiveBatchIds()
        {
            var ids = GetMonths().SelectMany(m => GetEffectiveOrders(m).Values.Select(b => b.Id)).ToHashSet();
            foreach (var g in repo.GetBatches().Where(b => b.Kind == ImportKind.Payroll).GroupBy(b => b.TargetMonth))
            {
                ids.Add(g.OrderByDescending(b => b.ImportedAt).First().Id);
            }
            var staff = repo.GetBatches().Where(b => b.Kind == ImportKind.Staff).OrderByDescending(b => b.ImportedAt).FirstOrDefault();
            if (staff != null)
            {
                ids.Add(staff.Id);
            }
            return ids;
        }

        public List<string> GetMonths() =>
            repo.GetBatches().Where(b => b.Kind == ImportKind.Attendance && b.TargetMonth != null)
                .Select(b => b.TargetMonth!).Distinct().OrderDescending().ToList();

        public MonthView? GetMonth(string? month)
        {
            var months = GetMonths();
            var target = string.IsNullOrEmpty(month) ? months.FirstOrDefault() : months.FirstOrDefault(m => m == month);
            return target == null ? null : Build(target);
        }

        public MonthView Build(string month)
        {
            var settings = repo.GetSettings();
            var orders = repo.GetOrders().ToDictionary(o => o.OrderNo);
            var staffMaster = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            var effective = GetEffectiveOrders(month);
            var batchIds = effective.Values.Select(b => b.Id).ToHashSet();
            var corrections = repo.GetCorrections()
                .Where(c => batchIds.Contains(c.BatchId))
                .OrderBy(c => c.CorrectedAt)
                .ToLookup(c => (c.EmployeeNo, c.OrderNo, c.Date));

            var view = new MonthView
            {
                Month = month,
                Settings = settings,
                Batches = effective.Values.Distinct().OrderBy(b => b.ImportedAt).ToList(),
            };
            var records = new List<(AttendanceRecord Record, ImportBatch Batch)>();
            foreach (var batch in view.Batches)
            {
                records.AddRange(repo.GetBatchData(batch.Id).Records
                    .Where(r => effective.GetValueOrDefault(r.OrderNo)?.Id == batch.Id)
                    .Select(r => (r, batch)));
            }

            foreach (var group in records.GroupBy(r => r.Record.EmployeeNo).OrderBy(g => g.Key.PadLeft(8, '0')))
            {
                var staff = staffMaster.GetValueOrDefault(group.Key) ?? new StaffMember { EmployeeNo = group.Key, Name = "（スタッフ情報なし）" };
                var staffMonth = new StaffMonth { Staff = staff };
                foreach (var byOrder in group.GroupBy(r => r.Record.OrderNo).OrderBy(g => g.Key))
                {
                    var batch = byOrder.First().Batch;
                    var order = orders.GetValueOrDefault(byOrder.Key) ?? new WorkOrder { OrderNo = byOrder.Key, Name = $"（未登録の受注 {byOrder.Key}）" };
                    var byDate = byOrder.Select(r => r.Record).ToDictionary(r => r.Date);
                    var sheet = new OrderSheet { Order = order, BatchId = batch.Id, FileName = batch.FileName, ImportedAt = batch.ImportedAt };
                    for (var date = view.FirstDay; date <= view.LastDay; date = date.AddDays(1))
                    {
                        var original = byDate.GetValueOrDefault(date);
                        var history = corrections[(group.Key, order.OrderNo, date)].ToList();
                        var active = history.LastOrDefault(c => !c.Revoked);
                        var values = active != null ? active.After : original?.Values;
                        var calc = LaborCalculator.Calculate(values, order, staff.PaidLeaveHoursPerDay);
                        sheet.Days.Add(new DayEntry
                        {
                            Date = date,
                            OrderNo = order.OrderNo,
                            Original = original,
                            Effective = values,
                            Correction = active,
                            History = history,
                            Calc = calc,
                            Warnings = LaborCalculator.Validate(values, calc),
                        });
                    }
                    staffMonth.Sheets.Add(sheet);
                }
                CheckAcrossOrders(staffMonth);
                foreach (var sheet in staffMonth.Sheets)
                {
                    sheet.Summary = Summarize(sheet.Days);
                }
                staffMonth.Summary = Summarize(staffMonth.AllDays.ToList());
                view.Staff.Add(staffMonth);
            }
            return view;
        }

        /// <summary>
        /// 掛け持ち（同じ日に複数の受注で勤務）のチェック。勤務時間の重複、同日の合計労働時間が8時間を超える日を要確認にする。
        /// あわせて退職日以降の勤務もチェックする。
        /// </summary>
        private static void CheckAcrossOrders(StaffMonth staffMonth)
        {
            var retire = staffMonth.Staff.Retired ? staffMonth.Staff.RetireDate : null;
            foreach (var day in staffMonth.AllDays.Where(d => d.Calc.WorkMinutes > 0 && retire != null && d.Date > retire))
            {
                day.Warnings.Add($"退職日（{retire:yyyy/MM/dd}）より後の勤務です");
            }
            if (staffMonth.Sheets.Count < 2)
            {
                return;
            }
            foreach (var sameDay in staffMonth.AllDays.Where(d => d.Calc.WorkMinutes > 0).GroupBy(d => d.Date).Where(g => g.Count() > 1))
            {
                var days = sameDay.ToList();
                var total = days.Sum(d => d.Calc.WorkMinutes);
                foreach (var day in days)
                {
                    var others = days.Where(d => d != day).ToList();
                    var (start, end) = Range(day.Effective!);
                    if (others.Any(o => Range(o.Effective!) is var r && r.Start < end && start < r.End))
                    {
                        day.Warnings.Add($"他の受注（{string.Join("・", others.Select(o => o.OrderNo))}）と勤務時間が重複しています");
                    }
                    else if (total > 480)
                    {
                        day.Warnings.Add($"掛け持ち：同日の合計労働時間が8時間超（{Fmt.Hours(total)}）。時間外の扱いを要確認");
                    }
                }
            }
        }

        private static (int Start, int End) Range(AttendanceValues values)
        {
            var start = values.Start ?? 0;
            var end = values.End ?? start;
            return (start, end <= start ? end + 24 * 60 : end);
        }

        private static MonthSummary Summarize(List<DayEntry> days)
        {
            var summary = new MonthSummary();
            var workDates = new HashSet<DateOnly>();
            var holidayDates = new HashSet<DateOnly>();
            foreach (var day in days)
            {
                switch (day.Effective?.Kind)
                {
                    case DayKind.Work: workDates.Add(day.Date); break;
                    case DayKind.HolidayWork: holidayDates.Add(day.Date); break;
                    case DayKind.PaidLeave: summary.PaidLeaveDays += 1; break;
                    case DayKind.HalfPaidLeave: summary.PaidLeaveDays += 0.5m; break;
                    case DayKind.Absence: summary.AbsenceDays++; break;
                    case DayKind.SpecialLeave: summary.SpecialLeaveDays++; break;
                    case DayKind.Leave2: summary.Leave2Days++; break;
                    case DayKind.Leave3: summary.Leave3Days++; break;
                    case DayKind.Leave4: summary.Leave4Days++; break;
                }
                summary.WorkMinutes += day.Calc.WorkMinutes;
                summary.BasicMinutes += day.Calc.BasicMinutes;
                summary.OvertimeMinutes += day.Calc.OvertimeMinutes;
                summary.NightMinutes += day.Calc.NightMinutes;
                summary.NightOvertimeMinutes += day.Calc.NightOvertimeMinutes;
                summary.HolidayMinutes += day.Calc.HolidayMinutes;
                summary.PaidLeaveMinutes += day.Calc.PaidLeaveMinutes;
                summary.BreakMinutes += day.Calc.WorkMinutes > 0 ? day.Effective!.BreakMinutes : 0;
                summary.WarningCount += day.Warnings.Count;
                summary.CorrectionCount += day.IsCorrected ? 1 : 0;
            }
            summary.WorkDays = workDates.Count;
            summary.HolidayWorkDays = holidayDates.Except(workDates).Count();
            return summary;
        }

        /// <summary>賃金台帳の対象年（支給年）</summary>
        public List<int> GetYears()
        {
            var settings = repo.GetSettings();
            var years = repo.GetBatches().Where(b => b.Kind == ImportKind.Payroll && b.TargetMonth != null).Select(b => int.Parse(b.TargetMonth![..4]))
                .Concat(GetMonths().Select(m => PayDateFor(DateOnly.ParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture), settings).Year));
            return years.Distinct().OrderDescending().ToList();
        }

        /// <summary>
        /// 指定年（支給年）の賃金台帳。スタッフ番号 → 支給月（1～12月）ごとの値。
        /// 給与計算書CSVを取り込んだ月はその値、未取込の月はタイムシートから試算する。
        /// </summary>
        public Dictionary<string, LedgerMonth?[]> GetLedgerBook(int year, string? employeeNo = null)
        {
            var settings = repo.GetSettings();
            var staff = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            var book = new Dictionary<string, LedgerMonth?[]>();
            LedgerMonth?[] Row(string emp) => book.TryGetValue(emp, out var row) ? row : book[emp] = new LedgerMonth?[12];

            var payrolls = repo.GetBatches().Where(b => b.Kind == ImportKind.Payroll && b.TargetMonth?.StartsWith($"{year}-") == true)
                .GroupBy(b => b.TargetMonth).Select(g => g.OrderByDescending(b => b.ImportedAt).First());
            foreach (var batch in payrolls)
            {
                foreach (var record in repo.GetBatchData(batch.Id).Payroll.Where(r => employeeNo == null || r.EmployeeNo == employeeNo))
                {
                    Row(record.EmployeeNo)[record.PayDate.Month - 1] = LedgerMonth.FromPayroll(record, staff.GetValueOrDefault(record.EmployeeNo), batch.FileName);
                }
            }

            var workMonths = GetMonths().Where(m => PayDateFor(DateOnly.ParseExact(m + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture), settings).Year == year);
            foreach (var month in workMonths)
            {
                var view = Build(month);
                var index = view.PayDate.Month - 1;
                foreach (var s in view.Staff.Where(s => employeeNo == null || s.Staff.EmployeeNo == employeeNo))
                {
                    var row = Row(s.Staff.EmployeeNo);
                    if (row[index] == null)
                    {
                        var estimate = LaborCalculator.Estimate(s, settings, view.PayDate, view.FirstDay, view.LastDay);
                        estimate.Source = view.SourceLabel;
                        row[index] = estimate;
                    }
                    else
                    {
                        // 給与計算書の月でも、勤務表の補正件数は表示する
                        row[index]!.CorrectionCount = s.Summary.CorrectionCount;
                    }
                }
            }

            // 前払い（速払金）：就業月の前払い額を、その月の給与（翌月支給）の賃金台帳に載せる
            var advances = repo.GetAdvances().Where(a => a.Status.IsActive())
                .GroupBy(a => (a.EmployeeNo, a.WorkMonth)).ToDictionary(g => g.Key, g => g.Sum(a => a.Amount));
            foreach (var (emp, row) in book)
            {
                for (var i = 0; i < 12; i++)
                {
                    if (row[i] != null && advances.TryGetValue((emp, $"{new DateOnly(year, i + 1, 1).AddMonths(-1):yyyy-MM}"), out var amount))
                    {
                        row[i]!.AdvancePayment = amount;
                    }
                }
            }
            return book;
        }

        /// <summary>スタッフ番号 → 就業先（取り込んだタイムシートのすべての月）</summary>
        public Dictionary<string, HashSet<string>> GetStaffWorkplaces()
        {
            var orders = repo.GetOrders().ToDictionary(o => o.OrderNo, o => o.Workplace);
            var result = new Dictionary<string, HashSet<string>>();
            foreach (var batch in repo.GetBatches().Where(b => b.Kind == ImportKind.Attendance))
            {
                foreach (var r in repo.GetBatchData(batch.Id).Records)
                {
                    if (!result.TryGetValue(r.EmployeeNo, out var set))
                    {
                        result[r.EmployeeNo] = set = [];
                    }
                    set.Add(orders.GetValueOrDefault(r.OrderNo, r.OrderNo));
                }
            }
            return result;
        }

        public string? AddCorrection(string month, string employeeNo, string orderNo, DateOnly date, AttendanceValues after,
            string reason, string? evidence, string user)
        {
            var view = GetMonth(month);
            var staffMonth = view?.Staff.FirstOrDefault(s => s.Staff.EmployeeNo == employeeNo);
            var sheet = staffMonth?.Sheets.FirstOrDefault(s => s.Order.OrderNo == orderNo);
            var day = sheet?.Days.FirstOrDefault(d => d.Date == date);
            if (view == null || staffMonth == null || sheet == null || day == null)
            {
                return "対象の勤務データが見つかりません。";
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return "補正理由を入力してください。";
            }
            if (after.Kind.HasTimes())
            {
                if (after.Kind.RequiresTimes() && (after.Start == null || after.End == null))
                {
                    return "出勤・休日出勤の場合は、始業と終業を入力してください（日をまたぐ場合は 32:00 のように入力）。";
                }
                if (after.Start != null && after.End != null && after.End <= after.Start)
                {
                    return "終業は始業より後の時刻を入力してください。日をまたぐ場合は 24:00 以降（例：翌朝8時 → 32:00）で入力します。";
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
            var current = day.Effective ?? new AttendanceValues(DayKind.NoWork, null, null, 0);
            if (after == current)
            {
                return "変更内容がありません。";
            }

            var now = Clock.Now;
            var correction = new Correction
            {
                Id = $"C{now:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                BatchId = sheet.BatchId,
                Month = view.Month,
                EmployeeNo = employeeNo,
                StaffName = staffMonth.Staff.Name,
                OrderNo = orderNo,
                OrderName = sheet.Order.Name,
                Date = date,
                Before = day.Effective,
                After = after,
                Reason = reason.Trim(),
                Evidence = string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim(),
                CorrectedBy = user,
                CorrectedAt = now,
            };
            repo.AddCorrection(correction);
            repo.AddLog(user, "補正", $"{staffMonth.Staff.Name}（{employeeNo}）{date:yyyy/MM/dd} {sheet.Order.Name}：" +
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
    }
}
