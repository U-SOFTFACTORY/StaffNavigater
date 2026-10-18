using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Import
{
    public class IndexModel(LaborRepository repo, LaborService labor, CsvImportService importer) : PageModel
    {
        private const long MaxFileSize = 5 * 1024 * 1024;

        public List<ImportBatch> Batches { get; private set; } = [];
        public HashSet<string> EffectiveIds { get; private set; } = [];

        public void OnGet()
        {
            Batches = repo.GetBatches().OrderByDescending(b => b.ImportedAt).ToList();
            EffectiveIds = labor.GetEffectiveBatches().Select(b => b.Id).ToHashSet();
        }

        public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "取り込むCSVファイルを選択してください。";
                return RedirectToPage();
            }
            if (file.Length > MaxFileSize)
            {
                TempData["Error"] = "ファイルサイズが大きすぎます（5MBまで）。";
                return RedirectToPage();
            }
            if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                TempData["Error"] = "CSVファイル（.csv）を選択してください。";
                return RedirectToPage();
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            var pending = importer.Parse(file.FileName, stream.ToArray(), User.Identity?.Name ?? "");
            repo.SavePending(pending);
            return RedirectToPage("/Import/Preview", new { token = pending.Token });
        }

        public IActionResult OnGetSample(string kind)
        {
            if (kind == "staff")
            {
                return File(DemoSeeder.ToShiftJis(DemoSeeder.StaffCsv(DemoSeeder.CreateStaff())), "text/csv", "スタッフ情報_サンプル.csv");
            }
            return File(DemoSeeder.SampleAttendanceCsv(), "text/csv", DemoSeeder.SampleAttendanceFileName);
        }
    }
}
