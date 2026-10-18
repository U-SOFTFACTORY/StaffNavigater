using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using StaffNavi.Services;

namespace StaffNavi.Pages.Account
{
    public class LoginModel(IOptionsMonitor<DemoOptions> demo, LaborRepository repo) : PageModel
    {
        [BindProperty]
        public string LoginId { get; set; } = "";

        [BindProperty]
        public string Password { get; set; } = "";

        [BindProperty(SupportsGet = true)]
        public string? ReturnUrl { get; set; }

        public string? Error { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var user = demo.CurrentValue.Users.FirstOrDefault(u =>
                string.Equals(u.LoginId, LoginId.Trim(), StringComparison.OrdinalIgnoreCase) && u.Password == Password);
            if (user == null)
            {
                Error = "ログインIDまたはパスワードが正しくありません。";
                return Page();
            }

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.LoginId),
                new Claim(ClaimTypes.Name, user.Name),
                new Claim(ClaimTypes.Role, user.Role),
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            repo.AddLog(user.Name, "ログイン", "ログインしました");

            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "/");
        }
    }
}
