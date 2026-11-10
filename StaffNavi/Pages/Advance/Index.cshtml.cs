using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using QRCoder;
using StaffNavi.Services;

namespace StaffNavi.Pages.Advance
{
    /// <summary>前払い状況（申請の受付状況・承認・振込データ作成）。新しい申請は画面を開いたまま自動で表示される</summary>
    public class IndexModel(AdvanceService advance) : PageModel
    {
        public AdvanceDashboard Dashboard { get; private set; } = new();
        public string AppUrl { get; private set; } = "";
        public string QrSvg { get; private set; } = "";

        public void OnGet()
        {
            Dashboard = advance.GetDashboard(Clock.Now);
            AppUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}/App/Login";
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(AppUrl, QRCodeGenerator.ECCLevel.M);
            QrSvg = new SvgQRCode(data).GetGraphic(6, "#000000", "#ffffff", true, SvgQRCode.SizingMode.ViewBoxAttribute);
        }

        /// <summary>自動更新用：申請の追加・状態の変更を確認する</summary>
        public IActionResult OnGetVersion() => new JsonResult(new { version = advance.Version() });

        /// <summary>自動更新用：集計と申請一覧を返す</summary>
        public IActionResult OnGetLive() => Partial("_AdvanceLive", advance.GetDashboard(Clock.Now));

        public IActionResult OnPostApprove(string id)
        {
            var error = advance.Approve(id, User.Identity?.Name ?? "", Clock.Now);
            TempData[error == null ? "Message" : "Error"] = error ?? "前払い申請を承認しました。";
            return RedirectToPage();
        }

        public IActionResult OnPostReject(string id, string? reason)
        {
            var error = advance.Reject(id, reason, User.Identity?.Name ?? "", Clock.Now);
            TempData[error == null ? "Message" : "Error"] = error ?? "前払い申請を却下しました。";
            return RedirectToPage();
        }

        /// <summary>本日振込分の振込データ（全銀フォーマット）をダウンロードし、振込済みにする</summary>
        public IActionResult OnPostTransfer()
        {
            var now = Clock.Now;
            var (file, count, _) = advance.CreateTransferFile(User.Identity?.Name ?? "", now);
            if (count == 0)
            {
                TempData["Error"] = "本日振込の対象となる申請はありません。";
                return RedirectToPage();
            }
            return File(file, "text/plain; charset=shift_jis", $"前払い振込データ_{now:yyyyMMdd_HHmm}.txt");
        }
    }
}
