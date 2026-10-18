using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Output
{
    public class PrintModel(LaborService labor, LaborRepository repo) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string Type { get; set; } = "timesheet";

        [BindProperty(SupportsGet = true)]
        public string? Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public int? Year { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Site { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Emp { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool Mark { get; set; } = true;

        public string DocumentTitle { get; private set; } = "";
        public LaborSettings Settings { get; private set; } = new();
        public MonthView? View { get; private set; }
        public List<StaffMonth> MonthStaff { get; private set; } = [];
        public List<(StaffMember Staff, List<(string Month, MonthView View, StaffMonth? Staff)> Columns)> Ledgers { get; private set; } = [];
        public DateTimeOffset PrintedAt { get; private set; }
        public string PrintedBy { get; private set; } = "";

        public string SiteLabel => string.IsNullOrEmpty(Site) ? "全拠点" : Site;

        public IActionResult OnGet()
        {
            Settings = repo.GetSettings();
            PrintedAt = Clock.Now;
            PrintedBy = User.Identity?.Name ?? "";
            string period;

            if (Type == "ledger")
            {
                DocumentTitle = "賃金台帳";
                var year = Year ?? labor.GetYears().FirstOrDefault();
                Year = year;
                period = $"{year}年";
                var targets = repo.GetStaff()
                    .Where(s => string.IsNullOrEmpty(Site) || s.Site == Site)
                    .Where(s => string.IsNullOrEmpty(Emp) || s.EmployeeNo == Emp)
                    .ToList();
                Ledgers = targets.Select(s => (s, labor.GetLedger(year, s.EmployeeNo))).ToList();
                if (Ledgers.Count == 0)
                {
                    return NotFound();
                }
            }
            else
            {
                Type = Type == "summary" ? "summary" : "timesheet";
                DocumentTitle = Type == "summary" ? "勤務実績集計表" : "勤務表（出勤簿）";
                View = labor.GetMonth(Month);
                if (View == null)
                {
                    return NotFound();
                }
                period = Fmt.Month(View.Month);
                MonthStaff = View.Staff
                    .Where(s => string.IsNullOrEmpty(Site) || s.Staff.Site == Site)
                    .Where(s => Type == "summary" || string.IsNullOrEmpty(Emp) || s.Staff.EmployeeNo == Emp)
                    .ToList();
            }

            var count = Type == "ledger" ? Ledgers.Count : MonthStaff.Count;
            var target = count == 1 && !string.IsNullOrEmpty(Emp) && Type != "summary"
                ? (Type == "ledger" ? Ledgers[0].Staff.Name : MonthStaff[0].Staff.Name)
                : $"{count}名";
            repo.AddLog(PrintedBy, "資料出力", $"{DocumentTitle}を出力しました（{period}、{SiteLabel}、{target}）", PrintedAt);
            return Page();
        }
    }
}
