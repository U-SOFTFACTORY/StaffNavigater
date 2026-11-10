using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace StaffNavi.Pages.App
{
    public class LogoutModel : PageModel
    {
        public IActionResult OnGet() => RedirectToPage("/App/Login");

        public async Task<IActionResult> OnPostAsync()
        {
            await HttpContext.SignOutAsync(AppPageModel.Scheme);
            return RedirectToPage("/App/Login");
        }
    }
}
