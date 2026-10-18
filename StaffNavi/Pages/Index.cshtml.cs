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
        public int ActiveCorrectionCount { get; private set; }
        public int RevokedCorrectionCount { get; private set; }
        public List<(StaffMonth Staff, DayEntry Day)> Alerts { get; private set; } = [];
        public List<Correction> RecentCorrections { get; private set; } = [];
        public List<ImportBatch> RecentBatches { get; private set; } = [];

        public void OnGet()
        {
            Latest = labor.GetMonth(null);
            var staff = repo.GetStaff();
            StaffCount = staff.Count;
            SiteCount = staff.Select(s => s.Site).Distinct().Count();
            MonthCount = labor.GetMonths().Count;

            var corrections = repo.GetCorrections();
            ActiveCorrectionCount = corrections.Count(c => !c.Revoked);
            RevokedCorrectionCount = corrections.Count(c => c.Revoked);
            RecentCorrections = corrections.OrderByDescending(c => c.CorrectedAt).Take(5).ToList();
            RecentBatches = repo.GetBatches().OrderByDescending(b => b.ImportedAt).Take(5).ToList();

            if (Latest != null)
            {
                Alerts = Latest.Staff
                    .SelectMany(s => s.Days.Where(d => d.Warnings.Count > 0).Select(d => (s, d)))
                    .OrderBy(a => a.d.Date)
                    .ToList();
            }
        }
    }
}
