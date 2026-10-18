using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Corrections
{
    public class IndexModel(LaborService labor, LaborRepository repo) : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public string? Month { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Emp { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Status { get; set; }

        public List<string> Months { get; private set; } = [];
        public List<StaffMember> Staff { get; private set; } = [];
        public List<Correction> Items { get; private set; } = [];

        public void OnGet()
        {
            Months = repo.GetCorrections().Select(c => c.Month).Union(labor.GetMonths()).Distinct().OrderDescending().ToList();
            Staff = repo.GetStaff();
            Items = repo.GetCorrections()
                .Where(c => string.IsNullOrEmpty(Month) || c.Month == Month)
                .Where(c => string.IsNullOrEmpty(Emp) || c.EmployeeNo == Emp)
                .Where(c => Status switch { "active" => !c.Revoked, "revoked" => c.Revoked, _ => true })
                .OrderByDescending(c => c.CorrectedAt)
                .ToList();
        }

        public IActionResult OnPostRevoke(string id, string? revokeReason)
        {
            var error = labor.RevokeCorrection(id, revokeReason ?? "", User.Identity?.Name ?? "");
            if (error != null)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Message"] = "補正を取り消しました。取消の記録も履歴に残ります。";
            }
            return RedirectToPage(new { Month, Emp, Status });
        }
    }
}
