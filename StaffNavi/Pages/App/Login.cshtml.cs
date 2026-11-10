using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Services;

namespace StaffNavi.Pages.App
{
    /// <summary>スタッフ用アプリのログイン（デモ：スタッフを選ぶか、スタッフ番号と暗証番号 1234 でログイン）</summary>
    public class LoginModel(AdvanceService advance, LaborRepository repo) : PageModel
    {
        public const string DemoPin = "1234";

        [BindProperty]
        public string EmployeeNo { get; set; } = "";

        [BindProperty]
        public string Pin { get; set; } = "";

        public string? Error { get; set; }

        public List<StaffAdvance> Picks { get; private set; } = [];

        public void OnGet() => LoadPicks();

        public async Task<IActionResult> OnPostAsync()
        {
            var no = EmployeeNo.Trim().TrimStart('0');
            if (Pin != DemoPin || advance.GetStaff(no, Clock.Now) == null)
            {
                Error = "スタッフ番号または暗証番号が正しくありません。";
                LoadPicks();
                return Page();
            }
            return await SignIn(no);
        }

        /// <summary>デモ用：一覧から選んだスタッフでログインする</summary>
        public async Task<IActionResult> OnPostPickAsync(string no)
        {
            if (advance.GetStaff(no, Clock.Now) == null)
            {
                return RedirectToPage();
            }
            return await SignIn(no);
        }

        private async Task<IActionResult> SignIn(string no)
        {
            var staff = repo.GetStaff().First(s => s.EmployeeNo == no);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, no), new Claim(ClaimTypes.Name, staff.Name)], AppPageModel.Scheme);
            await HttpContext.SignInAsync(AppPageModel.Scheme, new ClaimsPrincipal(identity));
            return RedirectToPage("/App/Index");
        }

        private void LoadPicks() =>
            Picks = advance.GetAllStaff(Clock.Now).Where(s => s.CanRequest).OrderByDescending(s => s.Available).Take(8).ToList();
    }
}
