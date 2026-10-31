using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Output
{
    public class IndexModel(LaborService labor, LaborRepository repo) : PageModel
    {
        public List<string> Months { get; private set; } = [];
        public List<int> Years { get; private set; } = [];
        public List<StaffMember> Staff { get; private set; } = [];
        public List<string> Sites { get; private set; } = [];
        public Dictionary<string, HashSet<string>> Workplaces { get; private set; } = [];
        public List<OperationLog> RecentOutputs { get; private set; } = [];

        public void OnGet()
        {
            Months = labor.GetMonths();
            Years = labor.GetYears();
            Workplaces = labor.GetStaffWorkplaces();
            Staff = repo.GetStaff().Where(s => Workplaces.ContainsKey(s.EmployeeNo)).OrderBy(s => s.EmployeeNo.PadLeft(8, '0')).ToList();
            Sites = Workplaces.Values.SelectMany(x => x).Distinct().Order().ToList();
            RecentOutputs = repo.GetLogs().Where(l => l.Category == "資料出力").OrderByDescending(l => l.At).Take(8).ToList();
        }

        /// <summary>補正後の勤怠を、スタッフナビゲーターの勤怠データCSVファイル取込形式で出力する（スタッフ×受注ごとに1行）</summary>
        public IActionResult OnGetSnAttendance(string? month, string? site)
        {
            var view = labor.GetMonth(month);
            if (view == null)
            {
                return RedirectToPage();
            }
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.AttendanceCsvHeaders };
            var targets = view.Staff
                .SelectMany(s => s.Sheets.Where(x => string.IsNullOrEmpty(site) || x.Order.Workplace == site).Select(x => (Staff: s, Sheet: x)))
                .ToList();
            rows.AddRange(targets.Select(t => StaffNavigatorFormat.AttendanceCsvRow(view, t.Staff, t.Sheet)));
            var siteLabel = string.IsNullOrEmpty(site) ? "全就業先" : site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力",
                $"スタッフナビゲーター取込用の勤怠データCSVを出力しました（{Fmt.Month(view.Month)}、{siteLabel}、{targets.Select(t => t.Staff).Distinct().Count()}名・{targets.Count}行、補正{targets.Sum(t => t.Sheet.Summary.CorrectionCount)}件反映）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"勤怠データ_{view.Month.Replace("-", "")}_{siteLabel}.csv");
        }

        /// <summary>全スタッフの賃金台帳を、賃金台帳CSV（先方の賃金台帳Excel「csv」シートと同じ61項目）で出力する</summary>
        public IActionResult OnGetSnLedger(int? year, string? site)
        {
            var target = year ?? labor.GetYears().FirstOrDefault();
            var workplaces = labor.GetStaffWorkplaces();
            var book = labor.GetLedgerBook(target)
                .Where(x => string.IsNullOrEmpty(site) || workplaces.GetValueOrDefault(x.Key)?.Contains(site) == true)
                .OrderBy(x => x.Key.PadLeft(8, '0'))
                .ToList();
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.LedgerCsvHeaders };
            rows.AddRange(book.SelectMany(x => x.Value.Where(c => c != null)).Select(c => StaffNavigatorFormat.LedgerCsvRow(c!)));
            var siteLabel = string.IsNullOrEmpty(site) ? "全就業先" : site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力", $"賃金台帳をCSVで出力しました（{target}年、{siteLabel}、{book.Count}名）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"賃金台帳_{target}_{siteLabel}.csv");
        }
    }
}
