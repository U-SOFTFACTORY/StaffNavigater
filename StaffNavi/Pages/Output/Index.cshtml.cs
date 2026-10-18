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
        public List<OperationLog> RecentOutputs { get; private set; } = [];

        public void OnGet()
        {
            Months = labor.GetMonths();
            Years = labor.GetYears();
            Staff = repo.GetStaff();
            Sites = Staff.Select(s => s.Site).Distinct().Order().ToList();
            RecentOutputs = repo.GetLogs().Where(l => l.Category == "資料出力").OrderByDescending(l => l.At).Take(8).ToList();
        }

        /// <summary>補正後の勤怠を、スタッフナビゲーターの勤怠データCSVファイル取込形式で出力する</summary>
        public IActionResult OnGetSnAttendance(string? month, string? site)
        {
            var view = labor.GetMonth(month);
            if (view == null)
            {
                return RedirectToPage();
            }
            var staff = view.Staff.Where(s => string.IsNullOrEmpty(site) || s.Staff.Site == site).ToList();
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.AttendanceCsvHeaders };
            rows.AddRange(staff.Select(s => StaffNavigatorFormat.AttendanceCsvRow(view, s)));
            var siteLabel = string.IsNullOrEmpty(site) ? "全拠点" : site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力",
                $"スタッフナビゲーター取込用の勤怠データCSVを出力しました（{Fmt.Month(view.Month)}、{siteLabel}、{staff.Count}名、補正{staff.Sum(s => s.Summary.CorrectionCount)}件反映）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"勤怠データ_{view.Month.Replace("-", "")}_{siteLabel}.csv");
        }

        /// <summary>全スタッフの賃金台帳を、スタッフナビゲーターの賃金台帳CSVファイル出力形式で出力する</summary>
        public IActionResult OnGetSnLedger(int? year, string? site)
        {
            var target = year ?? labor.GetYears().FirstOrDefault();
            var views = labor.GetEffectiveBatches()
                .Where(b => b.TargetMonth!.StartsWith($"{target}-"))
                .OrderBy(b => b.TargetMonth)
                .Select(labor.Build)
                .ToList();
            var rows = new List<IEnumerable<object?>> { StaffNavigatorFormat.LedgerCsvHeaders };
            var staff = views.SelectMany(v => v.Staff.Select(s => (View: v, Staff: s)))
                .Where(x => string.IsNullOrEmpty(site) || x.Staff.Staff.Site == site)
                .OrderBy(x => int.TryParse(x.Staff.Staff.EmployeeNo, out var no) ? no : int.MaxValue)
                .ThenBy(x => x.View.Month)
                .ToList();
            rows.AddRange(staff.Select(x => StaffNavigatorFormat.LedgerCsvRow(x.View, x.Staff)));
            var siteLabel = string.IsNullOrEmpty(site) ? "全拠点" : site;
            repo.AddLog(User.Identity?.Name ?? "", "資料出力",
                $"賃金台帳をCSV（スタッフナビゲーター形式）で出力しました（{target}年、{siteLabel}、{staff.Select(x => x.Staff.Staff.EmployeeNo).Distinct().Count()}名）");
            return File(StaffNavigatorFormat.ToShiftJis(CsvText.Write(rows)), "text/csv", $"賃金台帳_{target}_{siteLabel}.csv");
        }
    }
}
