using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Timesheet
{
    public class IndexModel(LaborService labor, LaborRepository repo) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Emp { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Site { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        /// <summary>要確認・掛け持ちのみ表示</summary>
        [BindProperty(SupportsGet = true)]
        public string? Filter { get; set; }

        public MonthView? View { get; private set; }
        public StaffMonth? Current { get; private set; }
        public List<string> Months { get; private set; } = [];
        public List<string> Sites { get; private set; } = [];
        public List<StaffMonth> StaffList { get; private set; } = [];
        public List<Correction> CurrentCorrections { get; private set; } = [];

        public void OnGet() => Load();

        public IActionResult OnPostCorrect(string order, string date, DayKind kind, string? start, string? end, int breakMinutes, string? reason, string? evidence)
        {
            if (Month == null || Emp == null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                TempData["Error"] = "補正対象が正しくありません。";
                return RedirectToPage(new { Month, Emp, Site, Filter, Q });
            }
            int? ParseClock(string? text) => Fmt.TryParseClock(text, out var m) ? m : null;
            var startMinutes = ParseClock(start);
            var endMinutes = ParseClock(end);
            // 終業が始業より前の時刻で入力された場合（例：22:00～8:00）は翌日として扱う
            if (startMinutes != null && endMinutes != null && endMinutes <= startMinutes && endMinutes < 24 * 60)
            {
                endMinutes += 24 * 60;
            }
            var values = new AttendanceValues(kind, startMinutes, endMinutes, breakMinutes);
            var error = labor.AddCorrection(Month, Emp, order, day, values, reason ?? "", evidence, User.Identity?.Name ?? "");
            if (error != null)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Message"] = $"{Fmt.Day(day)} の勤務データを補正しました。補正内容は補正履歴に記録されています。";
            }
            // 補正前の表示条件（就業先・要確認のみ等・検索）を引き継いで戻る
            return Redirect(Url.Page("/Timesheet/Index", new { Month, Emp, Site, Filter, Q }) + $"#o{order}-d{day.Day}");
        }

        public IActionResult OnGetCsv()
        {
            var view = labor.GetMonth(Month);
            if (view == null)
            {
                return RedirectToPage();
            }
            var staff = view.Staff.Where(s => string.IsNullOrEmpty(Site) || s.Workplaces.Contains(Site)).ToList();
            var rows = new List<IEnumerable<object?>>
            {
                new object?[]
                {
                    "スタッフ番号", "スタッフ氏名", "受注番号", "受注名称", "就業先", "日付", "曜日", "所定", "区分", "出欠", "始業", "終業", "休憩時",
                    "有給時", "基本", "普通残", "深夜", "深夜残", "休出", "合計", "補正", "補正理由", "要確認",
                },
            };
            foreach (var s in staff)
            {
                foreach (var sheet in s.Sheets.Where(x => string.IsNullOrEmpty(Site) || x.Order.Workplace == Site))
                {
                    foreach (var d in sheet.Days.Where(x => x.HasData))
                    {
                        var e = d.Effective!;
                        rows.Add(new object?[]
                        {
                            s.Staff.EmployeeNo, s.Staff.Name, sheet.Order.OrderNo, sheet.Order.Name, sheet.Order.Workplace,
                            d.Date.ToString("yyyy/MM/dd"), Fmt.Weekday(d.Date), d.Calendar, sheet.Order.ShiftLabel, e.Kind.Label(),
                            Fmt.Clock(e.Start), Fmt.Clock(e.End), d.Calc.WorkMinutes > 0 ? Fmt.HoursOrBlank(e.BreakMinutes) : "",
                            Fmt.HoursOrBlank(d.Calc.PaidLeaveMinutes), Fmt.HoursOrBlank(d.Calc.BasicMinutes), Fmt.HoursOrBlank(d.Calc.OvertimeMinutes),
                            Fmt.HoursOrBlank(d.Calc.NightMinutes), Fmt.HoursOrBlank(d.Calc.NightOvertimeMinutes), Fmt.HoursOrBlank(d.Calc.HolidayMinutes),
                            Fmt.HoursOrBlank(d.Calc.TotalMinutes), d.IsCorrected ? "補正あり" : "", d.Correction?.Reason, string.Join("／", d.Warnings),
                        });
                    }
                }
            }
            var siteLabel = string.IsNullOrEmpty(Site) ? "全就業先" : Site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力", $"勤務表をExcel（CSV）で出力しました（{Fmt.Month(view.Month)}、{siteLabel}、{staff.Count}名）");
            return File(CsvText.ToExcelBytes(CsvText.Write(rows)), "text/csv", $"勤務表_{view.Month.Replace("-", "")}_{siteLabel}.csv");
        }

        private void Load()
        {
            Months = labor.GetMonths();
            View = labor.GetMonth(Month);
            if (View == null)
            {
                return;
            }
            Month = View.Month;
            Sites = View.Sheets.Select(s => s.Order.Workplace).Distinct().Order().ToList();
            StaffList = View.Staff
                .Where(s => string.IsNullOrEmpty(Site) || s.Workplaces.Contains(Site))
                .Where(s => string.IsNullOrWhiteSpace(Q) || Matches(s, Q.Trim()))
                .Where(s => Filter switch { "alert" => s.Summary.WarningCount > 0, "multi" => s.Sheets.Count > 1, _ => true })
                .ToList();
            Current = StaffList.FirstOrDefault(s => s.Staff.EmployeeNo == Emp)
                ?? View.Staff.FirstOrDefault(s => s.Staff.EmployeeNo == Emp)
                ?? StaffList.FirstOrDefault();
            Emp = Current?.Staff.EmployeeNo;
            if (Current != null)
            {
                CurrentCorrections = Current.AllDays.SelectMany(d => d.History).OrderByDescending(c => c.CorrectedAt).ToList();
            }
        }

        private static bool Matches(StaffMonth s, string q)
        {
            var key = q.Replace(" ", "").Replace("　", "");
            return s.Staff.Name.Replace(" ", "").Contains(key) ||
                s.Staff.Kana.Replace(" ", "").Contains(key) ||
                s.Staff.EmployeeNo.Contains(key) ||
                s.Sheets.Any(x => x.Order.Workplace.Contains(key) || x.Order.OrderNo.Contains(key));
        }
    }
}
