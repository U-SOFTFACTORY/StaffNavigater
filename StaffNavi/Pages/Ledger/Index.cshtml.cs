using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Ledger
{
    public class IndexModel(LaborService labor, LaborRepository repo) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public int? Year { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Emp { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Site { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Q { get; set; }

        public List<int> Years { get; private set; } = [];
        public List<string> Sites { get; private set; } = [];
        public List<StaffMember> StaffList { get; private set; } = [];
        public StaffMember? Current { get; private set; }
        public Dictionary<string, HashSet<string>> Workplaces { get; private set; } = [];

        /// <summary>支給月（1～12月）ごとの賃金台帳</summary>
        public LedgerMonth?[] Columns { get; private set; } = new LedgerMonth?[12];

        public void OnGet()
        {
            Years = labor.GetYears();
            if (Years.Count == 0)
            {
                return;
            }
            Year = Year != null && Years.Contains(Year.Value) ? Year : Years.FirstOrDefault(y => y <= Clock.Now.Year, Years[0]);
            Workplaces = labor.GetStaffWorkplaces();
            Sites = Workplaces.Values.SelectMany(x => x).Distinct().Order().ToList();
            var book = labor.GetLedgerBook(Year.Value);
            var key = Q?.Replace(" ", "").Replace("　", "") ?? "";
            // 賃金台帳はその年に支給のあったスタッフが対象
            StaffList = repo.GetStaff()
                .Where(s => book.ContainsKey(s.EmployeeNo))
                .Where(s => string.IsNullOrEmpty(Site) || Workplaces.GetValueOrDefault(s.EmployeeNo)?.Contains(Site) == true)
                .Where(s => key == "" || s.Name.Replace(" ", "").Contains(key) || s.EmployeeNo.Contains(key))
                .OrderBy(s => s.EmployeeNo.PadLeft(8, '0'))
                .ToList();
            Current = StaffList.FirstOrDefault(s => s.EmployeeNo == Emp) ?? StaffList.FirstOrDefault();
            Emp = Current?.EmployeeNo;
            if (Current != null)
            {
                Columns = book[Current.EmployeeNo];
            }
        }

        /// <summary>賃金台帳CSV（先方の賃金台帳Excel「csv」シートと同じ61項目）で出力する</summary>
        public IActionResult OnGetCsv()
        {
            OnGet();
            if (Current == null)
            {
                return RedirectToPage();
            }
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.LedgerCsvHeaders };
            rows.AddRange(Columns.Where(c => c != null).Select(c => StaffNavigatorFormat.LedgerCsvRow(c!)));
            repo.AddLog(User.Identity?.Name ?? "", "資料出力", $"賃金台帳をCSVで出力しました（{Year}年、{Current.Name}）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"賃金台帳_{Year}_{Current.EmployeeNo}.csv");
        }
    }
}
