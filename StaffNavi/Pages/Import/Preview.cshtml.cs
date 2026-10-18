using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Import
{
    public class PreviewModel(LaborRepository repo, CsvImportService importer) : PageModel
    {
        public PendingImport Pending { get; private set; } = new();

        public IActionResult OnGet(string token)
        {
            var pending = repo.GetPending(token);
            if (pending == null)
            {
                TempData["Error"] = "取込確認中のデータが見つかりません。もう一度ファイルを選択してください。";
                return RedirectToPage("/Import/Index");
            }
            Pending = pending;
            return Page();
        }

        public IActionResult OnPostCommit(string token)
        {
            var pending = repo.GetPending(token);
            if (pending == null || !pending.CanCommit)
            {
                TempData["Error"] = "取込できないデータです。内容を確認してください。";
                return RedirectToPage("/Import/Index");
            }
            var batch = importer.Commit(pending, User.Identity?.Name ?? "");
            repo.DeletePending(token);
            TempData["Message"] = $"「{batch.FileName}」を取り込みました（{batch.RowCount}件）。";
            if (batch.Kind == ImportKind.Attendance)
            {
                return RedirectToPage("/Timesheet/Index", new { month = batch.TargetMonth });
            }
            return RedirectToPage("/Import/Index");
        }

        public IActionResult OnPostCancel(string token)
        {
            repo.DeletePending(token);
            TempData["Message"] = "取込を中止しました。";
            return RedirectToPage("/Import/Index");
        }
    }
}
