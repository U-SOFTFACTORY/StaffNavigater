using Microsoft.AspNetCore.Mvc;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.App
{
    public class RequestModel(AdvanceService advance, LaborRepository repo) : AppPageModel
    {
        public StaffAdvance Status { get; private set; } = new();
        public AdvanceSettings Settings { get; private set; } = new();
        public DateOnly Schedule { get; private set; }

        [BindProperty]
        public long Amount { get; set; }

        public string? Error { get; set; }

        public IActionResult OnGet() => Load() ? Page() : RedirectToPage("/App/Login");

        public IActionResult OnPost()
        {
            var (request, error) = advance.Apply(EmployeeNo, Amount, Clock.Now);
            if (request != null)
            {
                return RedirectToPage("/App/Done", new { id = request.Id });
            }
            Error = error;
            return Load() ? Page() : RedirectToPage("/App/Login");
        }

        private bool Load()
        {
            var status = advance.GetStaff(EmployeeNo, Clock.Now);
            if (status == null)
            {
                return false;
            }
            Status = status;
            Settings = repo.GetAdvanceSettings();
            Schedule = AdvanceService.ScheduleFor(Clock.Now, Settings);
            if (Amount == 0)
            {
                Amount = Math.Min(10000, status.Available / Settings.Unit * Settings.Unit);
            }
            return true;
        }
    }
}
