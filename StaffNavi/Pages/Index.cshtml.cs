using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages
{
    public class IndexModel(LaborService labor, LaborRepository repo) : PageModel
    {
        public MonthView? Latest { get; private set; }
        public int StaffCount { get; private set; }
        public int SiteCount { get; private set; }
        public int MonthCount { get; private set; }
        public int PayrollCount { get; private set; }
        public int MultiSiteCount { get; private set; }
        public int ActiveCorrectionCount { get; private set; }
        public int RevokedCorrectionCount { get; private set; }
        public List<(StaffMonth Staff, OrderSheet Sheet, DayEntry Day)> Alerts { get; private set; } = [];
        public List<Correction> RecentCorrections { get; private set; } = [];
        public List<ImportBatch> RecentBatches { get; private set; } = [];

        public void OnGet()
        {
            Latest = labor.GetMonth(null);
            StaffCount = Latest?.Staff.Count ?? 0;
            SiteCount = Latest?.Sheets.Select(s => s.Order.Workplace).Distinct().Count() ?? 0;
            MultiSiteCount = Latest?.Staff.Count(s => s.Sheets.Count > 1) ?? 0;
            MonthCount = labor.GetMonths().Count;
            PayrollCount = repo.GetBatches().Where(b => b.Kind == ImportKind.Payroll).Select(b => b.TargetMonth).Distinct().Count();

            var corrections = repo.GetCorrections();
            ActiveCorrectionCount = corrections.Count(c => !c.Revoked);
            RevokedCorrectionCount = corrections.Count(c => c.Revoked);
            RecentCorrections = corrections.OrderByDescending(c => c.CorrectedAt).Take(5).ToList();
            RecentBatches = repo.GetBatches().OrderByDescending(b => b.ImportedAt).Take(5).ToList();

            if (Latest != null)
            {
                Alerts = Latest.Staff
                    .SelectMany(s => s.Sheets.SelectMany(sheet => sheet.Days.Where(d => d.Warnings.Count > 0).Select(d => (s, sheet, d))))
                    .OrderBy(a => a.d.Date)
                    .ToList();
            }
        }
    }
}
