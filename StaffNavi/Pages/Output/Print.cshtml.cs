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
        public List<(StaffMember Staff, LedgerMonth?[] Columns)> Ledgers { get; private set; } = [];
        public DateTimeOffset PrintedAt { get; private set; }
        public string PrintedBy { get; private set; } = "";

        public string SiteLabel => string.IsNullOrEmpty(Site) ? "全就業先" : Site;

        /// <summary>出力対象の勤務表（就業先を指定した場合はその就業先の受注のみ）</summary>
        public IEnumerable<OrderSheet> SheetsOf(StaffMonth staff) => staff.Sheets.Where(s => string.IsNullOrEmpty(Site) || s.Order.Workplace == Site);

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
                var book = labor.GetLedgerBook(year, string.IsNullOrEmpty(Emp) ? null : Emp);
                var workplaces = labor.GetStaffWorkplaces();
                Ledgers = repo.GetStaff()
                    .Where(s => book.ContainsKey(s.EmployeeNo))
                    .Where(s => string.IsNullOrEmpty(Site) || workplaces.GetValueOrDefault(s.EmployeeNo)?.Contains(Site) == true)
                    .OrderBy(s => s.EmployeeNo.PadLeft(8, '0'))
                    .Select(s => (s, book[s.EmployeeNo]))
                    .ToList();
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
                    .Where(s => string.IsNullOrEmpty(Site) || s.Workplaces.Contains(Site))
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
