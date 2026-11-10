using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Advance
{
    /// <summary>前払いの条件（導入企業ごとに設定できる項目）</summary>
    public class SettingsModel(LaborRepository repo) : PageModel
    {
        [BindProperty]
        public int LimitPercent { get; set; }

        [BindProperty]
        public AdvanceSettings Input { get; set; } = new();

        public void OnGet()
        {
            Input = repo.GetAdvanceSettings();
            LimitPercent = (int)(Input.LimitRate * 100);
        }

        public IActionResult OnPost()
        {
            if (LimitPercent is < 10 or > 100 || Input.MinAmount < 1000 || Input.Unit < 1 || Input.MaxRequestsPerMonth is < 1 or > 31
                || Input.Fee < 0 || Input.AutoApproveLimit < 0 || Input.CutoffHour is < 0 or > 23)
            {
                TempData["Error"] = "入力内容を確認してください（上限は10～100%、最低申請額は1,000円以上、締め時刻は0～23時）。";
                return RedirectToPage();
            }
            Input.LimitRate = LimitPercent / 100m;
            repo.SaveAdvanceSettings(Input);
            repo.AddLog(User.Identity?.Name ?? "", "設定", $"前払いの条件を変更しました（上限{LimitPercent}%、手数料{Fmt.Yen(Input.Fee)}円・{(Input.FeePaidByStaff ? "スタッフ負担" : "会社負担")}、自動承認{Fmt.Yen(Input.AutoApproveLimit)}円以下）");
            TempData["Message"] = "前払いの条件を保存しました。スタッフ用アプリにもすぐに反映されます。";
            return RedirectToPage();
        }
    }
}
