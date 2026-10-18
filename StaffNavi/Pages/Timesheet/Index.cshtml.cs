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

        public MonthView? View { get; private set; }
        public StaffMonth? Current { get; private set; }
        public List<string> Months { get; private set; } = [];
        public List<string> Sites { get; private set; } = [];
        public List<StaffMonth> StaffList { get; private set; } = [];
        public List<Correction> CurrentCorrections { get; private set; } = [];

        public void OnGet() => Load();

        public IActionResult OnPostCorrect(string date, DayKind kind, string? start, string? end, int breakMinutes, string? reason, string? evidence)
        {
            if (Month == null || Emp == null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                TempData["Error"] = "補正対象が正しくありません。";
                return RedirectToPage(new { Month, Emp });
            }
            TimeOnly? ParseTime(string? text) =>
                TimeOnly.TryParseExact(text ?? "", ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

            var values = new AttendanceValues(kind, ParseTime(start), ParseTime(end), breakMinutes);
            var error = labor.AddCorrection(Month, Emp, day, values, reason ?? "", evidence, User.Identity?.Name ?? "");
            if (error != null)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Message"] = $"{Fmt.Day(day)} の勤務データを補正しました。補正内容は補正履歴に記録されています。";
            }
            return Redirect(Url.Page("/Timesheet/Index", new { Month, Emp }) + $"#d{day.Day}");
        }

        public IActionResult OnGetCsv()
        {
            var view = labor.GetMonth(Month);
            if (view == null)
            {
                return RedirectToPage();
            }
            var staff = view.Staff.Where(s => string.IsNullOrEmpty(Site) || s.Staff.Site == Site).ToList();
            var rows = new List<IEnumerable<object?>>
            {
                new object?[] { "スタッフ番号", "氏名", "拠点", "勤務日", "曜日", "勤務区分", "出勤時刻", "退勤時刻", "休憩(分)", "労働時間", "普通残", "深夜", "深夜残", "休出", "補正", "補正理由" },
            };
            foreach (var s in staff)
            {
                foreach (var d in s.Days)
                {
                    rows.Add(new object?[]
                    {
                        s.Staff.EmployeeNo, s.Staff.Name, s.Staff.Site, d.Date.ToString("yyyy/MM/dd"), Fmt.Weekday(d.Date),
                        d.Effective?.Kind.Label(), Fmt.Time(d.Effective?.Start), Fmt.Time(d.Effective?.End),
                        d.Effective?.Kind.IsWorking() == true ? d.Effective.BreakMinutes : null,
                        HoursOrEmpty(d.Calc.WorkMinutes), HoursOrEmpty(d.Calc.OvertimeMinutes),
                        HoursOrEmpty(d.Calc.NightMinutes), HoursOrEmpty(d.Calc.NightOvertimeMinutes), HoursOrEmpty(d.Calc.HolidayMinutes),
                        d.IsCorrected ? "補正あり" : "", d.Correction?.Reason,
                    });
                }
            }
            var siteLabel = string.IsNullOrEmpty(Site) ? "全拠点" : Site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力", $"勤務表をExcel（CSV）で出力しました（{Fmt.Month(view.Month)}、{siteLabel}、{staff.Count}名）");
            return File(CsvText.ToExcelBytes(CsvText.Write(rows)), "text/csv", $"勤務表_{view.Month.Replace("-", "")}_{siteLabel}.csv");
        }

        private static string HoursOrEmpty(int minutes) => minutes == 0 ? "" : Fmt.Hours(minutes);

        private void Load()
        {
            Months = labor.GetMonths();
            View = labor.GetMonth(Month);
            if (View == null)
            {
                return;
            }
            Month = View.Month;
            Sites = View.Staff.Select(s => s.Staff.Site).Distinct().Order().ToList();
            StaffList = View.Staff
                .Where(s => string.IsNullOrEmpty(Site) || s.Staff.Site == Site)
                .Where(s => string.IsNullOrWhiteSpace(Q) || Matches(s.Staff, Q.Trim()))
                .ToList();
            Current = StaffList.FirstOrDefault(s => s.Staff.EmployeeNo == Emp)
                ?? View.Staff.FirstOrDefault(s => s.Staff.EmployeeNo == Emp)
                ?? StaffList.FirstOrDefault();
            Emp = Current?.Staff.EmployeeNo;
            if (Current != null)
            {
                CurrentCorrections = Current.Days.SelectMany(d => d.History).OrderByDescending(c => c.CorrectedAt).ToList();
            }
        }

        private static bool Matches(StaffMember staff, string q) =>
            staff.Name.Replace(" ", "").Contains(q.Replace(" ", "").Replace("　", "")) ||
            staff.Kana.Replace(" ", "").Contains(q.Replace(" ", "").Replace("　", "")) ||
            staff.EmployeeNo.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            staff.Site.Contains(q);
    }
}
