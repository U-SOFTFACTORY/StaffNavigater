using Microsoft.AspNetCore.Mvc;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.App
{
    /// <summary>申請完了</summary>
    public class DoneModel(LaborRepository repo) : AppPageModel
    {
        public AdvanceRequest Item { get; private set; } = new();

        public IActionResult OnGet(string id)
        {
            var request = repo.GetAdvances().FirstOrDefault(r => r.Id == id && r.EmployeeNo == EmployeeNo);
            if (request == null)
            {
                return RedirectToPage("/App/Index");
            }
            Item = request;
            return Page();
        }
    }
}
