using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Import
{
    public class IndexModel(LaborRepository repo, LaborService labor, CsvImportService importer, DemoSeeder seeder) : PageModel
    {
        // スタッフ全データCSVは約1万人で13MBほどになるため、余裕をもって20MBまでとする
        private const long MaxFileSize = 20 * 1024 * 1024;

        public List<ImportBatch> Batches { get; private set; } = [];
        public HashSet<string> EffectiveIds { get; private set; } = [];

        public void OnGet()
        {
            Batches = repo.GetBatches().OrderByDescending(b => b.ImportedAt).ToList();
            EffectiveIds = labor.GetEffectiveBatchIds();
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
                TempData["Error"] = "ファイルサイズが大きすぎます（20MBまで）。";
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

        public IActionResult OnGetSample(string kind) => kind switch
        {
            "staff" => File(DemoSeeder.SampleStaffCsv(), "text/csv", "staff 全データ_サンプル.csv"),
            "payroll" => File(seeder.SamplePayrollCsv(), "text/csv", DemoSeeder.SamplePayrollFileName),
            _ => File(DemoSeeder.SampleTimesheetCsv(), "text/csv", DemoSeeder.SampleTimesheetFileName),
        };
    }
}
