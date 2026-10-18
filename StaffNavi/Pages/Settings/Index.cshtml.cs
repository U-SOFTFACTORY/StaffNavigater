using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Settings
{
    public class IndexModel(LaborRepository repo, DemoSeeder seeder) : PageModel
    {
        [BindProperty]
        public LaborSettings Input { get; set; } = new();

        public void OnGet() => Input = repo.GetSettings();

        public IActionResult OnPostSave()
        {
            if (string.IsNullOrWhiteSpace(Input.CompanyName) || Input.DailyStandardMinutes <= 0 || Input.PaidLeaveMinutes < 0 || Input.PayDayOfMonth is < 1 or > 31)
            {
                TempData["Error"] = "入力内容を確認してください。";
                return RedirectToPage();
            }
            Input.CompanyName = Input.CompanyName.Trim();
            repo.SaveSettings(Input);
            repo.AddLog(User.Identity?.Name ?? "", "設定", "計算設定を変更しました");
            TempData["Message"] = "設定を保存しました。勤務表・賃金台帳は新しい設定で再計算されます。";
            return RedirectToPage();
        }

        public IActionResult OnPostReset()
        {
            seeder.Reset(User.Identity?.Name ?? "");
            TempData["Message"] = "デモデータを初期状態に戻しました。";
            return RedirectToPage();
        }
    }
}
