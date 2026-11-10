using System.Collections.Concurrent;
using System.Text;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>前払い可能額の計算に使う1日分の勤務</summary>
    public class EarnedDay
    {
        public DateOnly Date { get; set; }
        public string Workplace { get; set; } = "";
        public string ShiftName { get; set; } = "";
        public DayKind Kind { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
        public int Minutes { get; set; }
        public long Pay { get; set; }
        /// <summary>打刻漏れなどで勤務時間が確定していない日（確定後に前払い可能額へ反映）</summary>
        public bool Pending { get; set; }
    }

    /// <summary>スタッフ1人の当月の前払い状況</summary>
    public class StaffAdvance
    {
        public StaffMember Staff { get; set; } = new();
        public string WorkMonth { get; set; } = "";
        public List<EarnedDay> Days { get; set; } = [];
        /// <summary>働いた分（見込の総支給額。交通費を除く）</summary>
        public long Earned { get; set; }
        /// <summary>前払いの上限（働いた分×上限率）</summary>
        public long Limit { get; set; }
        /// <summary>申請済みの額（却下を除く）</summary>
        public long Used { get; set; }
        public long Available { get; set; }
        public int RequestCount { get; set; }
        public List<AdvanceRequest> Requests { get; set; } = [];
        public DateOnly PayDate { get; set; }
        public BankAccount Bank { get; set; } = AdvanceService.BankFor("0");
        public string? BlockReason { get; set; }

        public bool CanRequest => BlockReason == null;
        public string MainWorkplace => Days.GroupBy(d => d.Workplace).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
        public int WorkDays => Days.Count(d => d.Pay > 0);
        public int WorkMinutes => Days.Sum(d => d.Minutes);
    }

    /// <summary>前払い管理画面の集計</summary>
    public class AdvanceDashboard
    {
        public string WorkMonth { get; set; } = "";
        public DateOnly Today { get; set; }
        public AdvanceSettings Settings { get; set; } = new();
        public List<AdvanceRequest> Requests { get; set; } = [];
        public List<StaffAdvance> Staff { get; set; } = [];
        public long MonthTotal { get; set; }
        public long PrevMonthTotal { get; set; }
        public int RequestCount { get; set; }
        public int UserCount { get; set; }
        public int EligibleCount { get; set; }
        public long FeeTotal { get; set; }
        public int PendingCount { get; set; }
        public int TodayCount { get; set; }
        public long TodayTransfer { get; set; }
        public int LaterCount { get; set; }
        public long LaterTransfer { get; set; }
        /// <summary>全スタッフが上限まで申請した場合に、あと必要になる資金（資金準備の目安）</summary>
        public long RemainingCapacity { get; set; }
        /// <summary>日ごとの申請額（1日～月末）</summary>
        public long[] Daily { get; set; } = [];
        public string Version { get; set; } = "";

        public int UsageRate => EligibleCount == 0 ? 0 : (int)Math.Round(UserCount * 100m / EligibleCount);
    }

    /// <summary>
    /// 前払い（給与の前払い）の計算・申請・承認・振込データ作成。
    /// 前払いの原資は導入企業（スタッフの雇用主）の資金で、本システムは申請の受付・審査・振込データ作成を行う。
    /// </summary>
    public class AdvanceService(LaborRepository repo)
    {
        /// <summary>デモ用：月ごとのタイムシート（デモデータと同じ作り方。当月分は日々の勤怠の速報として使う）</summary>
        private static readonly ConcurrentDictionary<string, List<AttendanceRecord>> Timesheets = new();

        private static List<AttendanceRecord> MonthRecords(int year, int month) =>
            Timesheets.GetOrAdd($"{year}-{month:00}", _ => DemoSeeder.CreateTimesheet(DemoSeeder.CreateStaff(), DemoSeeder.CreateOrders(), year, month,
                (year, month) == (DemoSeeder.LatestYear, DemoSeeder.LatestMonth)));

        public static string MonthKey(DateOnly date) => $"{date.Year}-{date.Month:00}";

        public static DateOnly TodayOf(DateTimeOffset now) => DateOnly.FromDateTime(now.DateTime);

        /// <summary>スタッフ1人の当月の前払い状況（now の前日までの勤務を対象にする）</summary>
        public StaffAdvance? GetStaff(string employeeNo, DateTimeOffset now)
        {
            var staff = repo.GetStaff().FirstOrDefault(s => s.EmployeeNo == employeeNo);
            return staff == null ? null : Compute(staff, now, repo.GetAdvances(), Context());
        }

        public List<StaffAdvance> GetAllStaff(DateTimeOffset now)
        {
            var context = Context();
            var requests = repo.GetAdvances();
            return repo.GetStaff().Select(s => Compute(s, now, requests, context)).ToList();
        }

        private (AdvanceSettings Advance, LaborSettings Labor, Dictionary<string, WorkOrder> Orders) Context() =>
            (repo.GetAdvanceSettings(), repo.GetSettings(), repo.GetOrders().ToDictionary(o => o.OrderNo));

        private static StaffAdvance Compute(StaffMember staff, DateTimeOffset now, List<AdvanceRequest> requests,
            (AdvanceSettings Advance, LaborSettings Labor, Dictionary<string, WorkOrder> Orders) context)
        {
            var (adv, labor, orders) = context;
            var today = TodayOf(now);
            var first = new DateOnly(today.Year, today.Month, 1);
            var result = new StaffAdvance
            {
                Staff = staff,
                WorkMonth = MonthKey(today),
                PayDate = LaborService.PayDateFor(first, labor),
                Bank = BankFor(staff.EmployeeNo),
            };
            foreach (var r in MonthRecords(today.Year, today.Month).Where(r => r.EmployeeNo == staff.EmployeeNo && r.Date < today).OrderBy(r => r.Date))
            {
                if (!orders.TryGetValue(r.OrderNo, out var order))
                {
                    continue;
                }
                var calc = LaborCalculator.Calculate(r.Values, order, staff.PaidLeaveHoursPerDay);
                result.Days.Add(new EarnedDay
                {
                    Date = r.Date,
                    Workplace = order.Workplace,
                    ShiftName = order.ShiftName,
                    Kind = r.Kind,
                    Start = r.Start,
                    End = r.End,
                    Minutes = calc.TotalMinutes,
                    Pay = LaborCalculator.DayPay(calc, order.HourlyWage, labor),
                    Pending = r.Kind.RequiresTimes() && (r.Start == null || r.End == null),
                });
            }
            result.Earned = result.Days.Sum(d => d.Pay);
            result.Limit = FloorTo(result.Earned * adv.LimitRate, adv.Unit);
            result.Requests = requests.Where(q => q.EmployeeNo == staff.EmployeeNo && q.WorkMonth == result.WorkMonth && q.RequestedAt <= now)
                .OrderByDescending(q => q.RequestedAt).ToList();
            result.Used = result.Requests.Where(q => q.Status.IsActive()).Sum(q => q.Amount);
            result.RequestCount = result.Requests.Count(q => q.Status.IsActive());
            result.Available = Math.Max(0, result.Limit - result.Used);

            if (staff.Retired && staff.RetireDate < today)
            {
                result.BlockReason = "退職済みのため申請できません";
            }
            else if (result.RequestCount >= adv.MaxRequestsPerMonth)
            {
                result.BlockReason = $"今月の申請回数の上限（{adv.MaxRequestsPerMonth}回）に達しました";
            }
            else if (result.Available < adv.MinAmount)
            {
                result.BlockReason = result.Earned == 0 ? "今月の勤務実績が確定すると申請できます" : $"前払い可能額が最低申請額（{Fmt.Yen(adv.MinAmount)}円）に達していません";
            }
            return result;
        }

        private static long FloorTo(decimal value, long unit) => unit <= 0 ? (long)value : (long)Math.Floor(value / unit) * unit;

        /// <summary>振込予定日（締め時刻までの申請は当日、それ以降・土日は翌営業日。祝日はデモでは考慮しない）</summary>
        public static DateOnly ScheduleFor(DateTimeOffset at, AdvanceSettings settings)
        {
            var date = TodayOf(at);
            if (at.Hour >= settings.CutoffHour || IsHoliday(date))
            {
                date = date.AddDays(1);
            }
            while (IsHoliday(date))
            {
                date = date.AddDays(1);
            }
            return date;
        }

        private static bool IsHoliday(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        /// <summary>前払いの申請（スマホアプリから）。エラーの場合はメッセージを返す</summary>
        public (AdvanceRequest? Request, string? Error) Apply(string employeeNo, long amount, DateTimeOffset now, string channel = "スマホアプリ")
        {
            var adv = repo.GetAdvanceSettings();
            var status = GetStaff(employeeNo, now);
            if (status == null)
            {
                return (null, "スタッフ情報が見つかりません。");
            }
            if (status.BlockReason != null)
            {
                return (null, status.BlockReason + "。");
            }
            if (amount < adv.MinAmount || amount % adv.Unit != 0)
            {
                return (null, $"申請額は{Fmt.Yen(adv.MinAmount)}円以上、{Fmt.Yen(adv.Unit)}円単位で入力してください。");
            }
            if (amount > status.Available)
            {
                return (null, $"申請額が前払い可能額（{Fmt.Yen(status.Available)}円）を超えています。");
            }

            var request = NewRequest(status, amount, now, adv, channel);
            repo.UpdateAdvances(list => list.Add(request));
            repo.AddLog(status.Staff.Name, "前払い", $"前払いを申請しました（{Fmt.Yen(amount)}円、{(request.AutoApproved ? $"自動承認・{request.ScheduledDate:M/d}振込予定" : "承認待ち")}）", now);
            return (request, null);
        }

        private static AdvanceRequest NewRequest(StaffAdvance status, long amount, DateTimeOffset at, AdvanceSettings adv, string channel)
        {
            var auto = amount <= adv.AutoApproveLimit;
            var fee = adv.Fee;
            return new AdvanceRequest
            {
                Id = $"A{at:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                EmployeeNo = status.Staff.EmployeeNo,
                StaffName = status.Staff.Name,
                Workplace = status.MainWorkplace,
                WorkMonth = status.WorkMonth,
                Amount = amount,
                Fee = fee,
                TransferAmount = adv.FeePaidByStaff ? amount - fee : amount,
                AvailableAtRequest = status.Available,
                RequestedAt = at,
                ScheduledDate = ScheduleFor(at, adv),
                Status = auto ? AdvanceStatus.Approved : AdvanceStatus.Requested,
                AutoApproved = auto,
                ApprovedAt = auto ? at : null,
                ApprovedBy = auto ? "自動承認" : null,
                Channel = channel,
                UpdatedAt = at,
            };
        }

        public string? Approve(string id, string user, DateTimeOffset now)
        {
            AdvanceRequest? target = null;
            var adv = repo.GetAdvanceSettings();
            repo.UpdateAdvances(list =>
            {
                target = list.FirstOrDefault(r => r.Id == id && r.Status == AdvanceStatus.Requested);
                if (target != null)
                {
                    target.Status = AdvanceStatus.Approved;
                    target.ApprovedBy = user;
                    target.ApprovedAt = now;
                    target.ScheduledDate = ScheduleFor(now, adv);
                    target.UpdatedAt = now;
                }
            });
            if (target == null)
            {
                return "対象の申請が見つからないか、すでに処理済みです。";
            }
            repo.AddLog(user, "前払い", $"{target.StaffName}（{target.EmployeeNo}）の前払い申請 {Fmt.Yen(target.Amount)}円を承認しました（{target.ScheduledDate:M/d}振込予定）", now);
            return null;
        }

        public string? Reject(string id, string? reason, string user, DateTimeOffset now)
        {
            AdvanceRequest? target = null;
            repo.UpdateAdvances(list =>
            {
                target = list.FirstOrDefault(r => r.Id == id && r.Status == AdvanceStatus.Requested);
                if (target != null)
                {
                    target.Status = AdvanceStatus.Rejected;
                    target.RejectReason = string.IsNullOrWhiteSpace(reason) ? "勤務実績の確認が必要なため" : reason.Trim();
                    target.ApprovedBy = user;
                    target.ApprovedAt = now;
                    target.UpdatedAt = now;
                }
            });
            if (target == null)
            {
                return "対象の申請が見つからないか、すでに処理済みです。";
            }
            repo.AddLog(user, "前払い", $"{target.StaffName}（{target.EmployeeNo}）の前払い申請 {Fmt.Yen(target.Amount)}円を却下しました（理由：{target.RejectReason}）", now);
            return null;
        }

        /// <summary>本日振込分の振込データ（全銀協フォーマット・総合振込）を作成し、対象の申請を振込済みにする</summary>
        public (byte[] File, int Count, long Total) CreateTransferFile(string user, DateTimeOffset now)
        {
            var today = TodayOf(now);
            var staff = repo.GetStaff().ToDictionary(s => s.EmployeeNo);
            var targets = new List<AdvanceRequest>();
            repo.UpdateAdvances(list =>
            {
                foreach (var r in list.Where(r => r.Status == AdvanceStatus.Approved && r.ScheduledDate <= today))
                {
                    r.Status = AdvanceStatus.Transferred;
                    r.TransferredAt = now;
                    r.UpdatedAt = now;
                    targets.Add(r);
                }
            });
            var company = repo.GetSettings().CompanyName;
            var file = ZenginFormat.Create(today, targets.Select(r => (BankFor(r.EmployeeNo), staff.GetValueOrDefault(r.EmployeeNo)?.Kana ?? "", r.TransferAmount, r.EmployeeNo)));
            if (targets.Count > 0)
            {
                repo.AddLog(user, "前払い", $"前払いの振込データ（全銀フォーマット）を作成しました（{targets.Count}件、{Fmt.Yen(targets.Sum(t => t.TransferAmount))}円、振込元：{company}）", now);
            }
            return (file, targets.Count, targets.Sum(t => t.TransferAmount));
        }

        public int PendingCount() => repo.GetAdvances().Count(r => r.Status == AdvanceStatus.Requested);

        /// <summary>画面の自動更新用（申請の追加・状態の変更で変わる値）</summary>
        public string Version()
        {
            var list = repo.GetAdvances();
            return $"{list.Count}-{(list.Count == 0 ? 0 : list.Max(r => r.UpdatedAt).ToUnixTimeMilliseconds())}";
        }

        public AdvanceDashboard GetDashboard(DateTimeOffset now)
        {
            var today = TodayOf(now);
            var month = MonthKey(today);
            var prevMonth = MonthKey(today.AddMonths(-1));
            var all = repo.GetAdvances();
            var monthRequests = all.Where(r => r.WorkMonth == month).OrderByDescending(r => r.RequestedAt).ToList();
            var active = monthRequests.Where(r => r.Status.IsActive()).ToList();
            var staff = GetAllStaff(now).Where(s => s.Earned > 0 || s.Requests.Count > 0).OrderByDescending(s => s.Available).ToList();
            var daily = new long[DateTime.DaysInMonth(today.Year, today.Month)];
            foreach (var r in active)
            {
                daily[r.RequestedAt.Day - 1] += r.Amount;
            }
            var waiting = all.Where(r => r.Status == AdvanceStatus.Approved).ToList();
            return new AdvanceDashboard
            {
                WorkMonth = month,
                Today = today,
                Settings = repo.GetAdvanceSettings(),
                Requests = monthRequests,
                Staff = staff,
                MonthTotal = active.Sum(r => r.Amount),
                PrevMonthTotal = all.Where(r => r.WorkMonth == prevMonth && r.Status.IsActive()).Sum(r => r.Amount),
                RequestCount = active.Count,
                UserCount = active.Select(r => r.EmployeeNo).Distinct().Count(),
                EligibleCount = staff.Count(s => !s.Staff.Retired),
                FeeTotal = active.Sum(r => r.Fee),
                PendingCount = all.Count(r => r.Status == AdvanceStatus.Requested),
                TodayCount = waiting.Count(r => r.ScheduledDate <= today),
                TodayTransfer = waiting.Where(r => r.ScheduledDate <= today).Sum(r => r.TransferAmount),
                LaterCount = waiting.Count(r => r.ScheduledDate > today),
                LaterTransfer = waiting.Where(r => r.ScheduledDate > today).Sum(r => r.TransferAmount),
                RemainingCapacity = staff.Where(s => s.CanRequest).Sum(s => s.Available),
                Daily = daily,
                Version = Version(),
            };
        }

        /// <summary>スタッフの振込先口座（デモ用の架空の銀行・口座。スタッフ番号から決める）</summary>
        public static BankAccount BankFor(string employeeNo)
        {
            (string Code, string Name, string Kana)[] banks = [("9901", "みどり銀行", "ﾐﾄﾞﾘｷﾞﾝｺｳ"), ("9902", "あおば銀行", "ｱｵﾊﾞｷﾞﾝｺｳ"), ("9903", "サンプル信用金庫", "ｻﾝﾌﾟﾙｼﾝｷﾝ")];
            (string Code, string Name, string Kana)[] branches = [("101", "多摩支店", "ﾀﾏ"), ("102", "府中支店", "ﾌﾁｭｳ"), ("201", "町田支店", "ﾏﾁﾀﾞ"), ("301", "川崎支店", "ｶﾜｻｷ")];
            var n = long.TryParse(employeeNo, out var v) ? v : 0;
            var bank = banks[n % banks.Length];
            var branch = branches[n / 3 % branches.Length];
            return new BankAccount(bank.Code, bank.Name, bank.Kana, branch.Code, branch.Name, branch.Kana, (1000000 + n * 7919 % 9000000).ToString()[..7]);
        }

        /// <summary>
        /// デモ用の前払い履歴（9月・10月・当月）。当月分は現在時刻までの申請を作る。
        /// デモデータの初期化で作り直されるので、お打ち合わせ当日の朝に初期化すると当日の申請・承認待ちが表示される。
        /// </summary>
        public void SeedHistory(DateTimeOffset now)
        {
            var context = Context();
            var staff = repo.GetStaff().Where(s => !s.Retired).ToList();
            var today = TodayOf(now);
            var current = new DateOnly(today.Year, today.Month, 1);
            var months = new List<DateOnly> { new(2026, 9, 1), new(2026, 10, 1) };
            if (current > months[^1])
            {
                months.Add(current);
            }
            var requests = new List<AdvanceRequest>();
            long[] amounts = [5000, 10000, 10000, 15000, 20000, 30000];
            foreach (var month in months)
            {
                var rng = new Random(month.Year * 100 + month.Month + 7);
                var last = month == current ? today : month.AddMonths(1).AddDays(-1);
                foreach (var s in staff.Where(_ => rng.NextDouble() < 0.55))
                {
                    // 当月は月初から今日まで、過去の月は6日～28日の間に申請する
                    var firstDay = month == current ? 2 : 6;
                    var lastDay = Math.Max(firstDay, Math.Min(28, last.Day));
                    var days = Enumerable.Range(0, rng.Next(1, 4)).Select(_ => rng.Next(firstDay, lastDay + 1)).Distinct().Order();
                    foreach (var day in days)
                    {
                        if (day > last.Day)
                        {
                            continue;
                        }
                        var at = new DateTimeOffset(month.Year, month.Month, day, rng.Next(7, 23), rng.Next(0, 60), 0, now.Offset);
                        if (at >= now)
                        {
                            continue;
                        }
                        var status = Compute(s, at, requests, context);
                        var options = amounts.Where(a => a <= status.Available).ToArray();
                        if (status.BlockReason != null || options.Length == 0)
                        {
                            continue;
                        }
                        var amount = options[rng.Next(options.Length)];
                        var request = NewRequest(status, amount, at, context.Advance, rng.NextDouble() < 0.9 ? "スマホアプリ" : "管理画面（代理申請）");
                        if (!request.AutoApproved)
                        {
                            request.Status = AdvanceStatus.Approved;
                            request.ApprovedBy = DemoSeeder.AdminName;
                            request.ApprovedAt = at.AddMinutes(35);
                            request.ScheduledDate = ScheduleFor(at.AddMinutes(35), context.Advance);
                        }
                        if (request.ScheduledDate < today)
                        {
                            request.Status = AdvanceStatus.Transferred;
                            request.TransferredAt = new DateTimeOffset(request.ScheduledDate.ToDateTime(new TimeOnly(15, 30)), now.Offset);
                        }
                        request.UpdatedAt = request.TransferredAt ?? request.ApprovedAt ?? at;
                        requests.Add(request);
                    }
                }
            }

            // 承認待ち（自動承認の上限を超える申請）を当日分として2件用意する
            var pendingAt = now.AddMinutes(-48);
            foreach (var s in staff.Select(s => Compute(s, pendingAt, requests, context)).Where(s => s.CanRequest && s.Available >= 25000).Take(2))
            {
                var request = NewRequest(s, s.Available >= 30000 ? 30000 : 25000, pendingAt, context.Advance, "スマホアプリ");
                requests.Add(request);
                pendingAt = pendingAt.AddMinutes(31);
            }
            repo.UpdateAdvances(list => list.AddRange(requests.OrderBy(r => r.RequestedAt)));
        }
    }

    /// <summary>全銀協規定フォーマット（総合振込・固定長120バイト、Shift_JIS）</summary>
    public static class ZenginFormat
    {
        private const string ClientCode = "1234567890";
        private const string ClientKana = "ｶ)ｻﾝﾌﾟﾙｴﾅｼﾞｰ";

        public static byte[] Create(DateOnly date, IEnumerable<(BankAccount Bank, string Kana, long Amount, string CustomerCode)> rows)
        {
            var list = rows.ToList();
            var sb = new StringBuilder();
            void Line(string text) => sb.Append(text.PadRight(120)[..120]).Append("\r\n");

            // ヘッダー（振込元：デモ用の架空の口座）
            Line("1" + "21" + "0" + ClientCode + Pad(ClientKana, 40) + date.ToString("MMdd") + "9901" + Pad("ﾐﾄﾞﾘｷﾞﾝｺｳ", 15) + "001" + Pad("ﾎﾝﾃﾝ", 15) + "1" + "1234567");
            foreach (var (bank, kana, amount, code) in list)
            {
                Line("2" + bank.BankCode + Pad(bank.BankKana, 15) + bank.BranchCode + Pad(bank.BranchKana, 15) + "0000" + "1" + bank.Number
                    + Pad(ToHalfKana(kana), 30) + amount.ToString("0000000000") + "0" + code.PadLeft(10, '0') + new string(' ', 10) + "7" + " ");
            }
            Line("8" + list.Count.ToString("000000") + list.Sum(r => r.Amount).ToString("000000000000"));
            Line("9");
            return Encoding.GetEncoding(932).GetBytes(sb.ToString());
        }

        private static string Pad(string text, int length) => text.Length >= length ? text[..length] : text.PadRight(length);

        private const string Full = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲンァィゥェォャュョッー　";
        private const string Half = "ｱｲｳｴｵｶｷｸｹｺｻｼｽｾｿﾀﾁﾂﾃﾄﾅﾆﾇﾈﾉﾊﾋﾌﾍﾎﾏﾐﾑﾒﾓﾔﾕﾖﾗﾘﾙﾚﾛﾜｦﾝｧｨｩｪｫｬｭｮｯｰ ";

        /// <summary>全角カタカナを半角カナにする（濁点・半濁点は分解して ﾞ ﾟ にする）</summary>
        public static string ToHalfKana(string text)
        {
            var sb = new StringBuilder();
            foreach (var c in text.Normalize(NormalizationForm.FormD))
            {
                var i = Full.IndexOf(c);
                sb.Append(i >= 0 ? Half[i] : c switch { '゙' => 'ﾞ', '゚' => 'ﾟ', ' ' => ' ', _ => c < 0x80 ? c : ' ' });
            }
            return sb.ToString();
        }
    }
}
