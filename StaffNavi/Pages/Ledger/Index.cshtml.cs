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

        public List<int> Years { get; private set; } = [];
        public List<string> Sites { get; private set; } = [];
        public List<StaffMember> StaffList { get; private set; } = [];
        public StaffMember? Current { get; private set; }
        public List<(string Month, MonthView View, StaffMonth? Staff)> Columns { get; private set; } = [];

        public void OnGet()
        {
            Years = labor.GetYears();
            if (Years.Count == 0)
            {
                return;
            }
            Year = Year != null && Years.Contains(Year.Value) ? Year : Years[0];
            var staff = repo.GetStaff();
            Sites = staff.Select(s => s.Site).Distinct().Order().ToList();
            StaffList = staff.Where(s => string.IsNullOrEmpty(Site) || s.Site == Site).ToList();
            Current = StaffList.FirstOrDefault(s => s.EmployeeNo == Emp) ?? StaffList.FirstOrDefault();
            Emp = Current?.EmployeeNo;
            if (Current != null)
            {
                Columns = labor.GetLedger(Year.Value, Current.EmployeeNo);
            }
        }

        /// <summary>スタッフナビゲーターの賃金台帳CSVファイル出力形式で出力する</summary>
        public IActionResult OnGetCsv()
        {
            OnGet();
            if (Current == null)
            {
                return RedirectToPage();
            }
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.LedgerCsvHeaders };
            rows.AddRange(Columns.Where(c => c.Staff != null).Select(c => StaffNavigatorFormat.LedgerCsvRow(c.View, c.Staff!)));
            repo.AddLog(User.Identity?.Name ?? "", "資料出力", $"賃金台帳をCSV（スタッフナビゲーター形式）で出力しました（{Year}年、{Current.Name}）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"賃金台帳_{Year}_{Current.EmployeeNo}.csv");
        }
    }
}
