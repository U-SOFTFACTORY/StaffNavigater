using Microsoft.AspNetCore.Mvc;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.App
{
    public class IndexModel(AdvanceService advance, LaborRepository repo) : AppPageModel
    {
        public StaffAdvance Status { get; private set; } = new();
        public AdvanceSettings Settings { get; private set; } = new();

        public IActionResult OnGet()
        {
            var status = advance.GetStaff(EmployeeNo, Clock.Now);
            if (status == null)
            {
                return RedirectToPage("/App/Login");
            }
            Status = status;
            Settings = repo.GetAdvanceSettings();
            return Page();
        }
    }
}
