using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.Import
{
    public class DetailModel(LaborRepository repo) : PageModel
    {
        public ImportBatch Batch { get; private set; } = new();
        public BatchData Data { get; private set; } = new();
        public int CorrectionCount { get; private set; }

        public IActionResult OnGet(string id)
        {
            var batch = repo.GetBatch(id);
            if (batch == null)
            {
                return NotFound();
            }
            Batch = batch;
            Data = repo.GetBatchData(id);
            CorrectionCount = repo.GetCorrections().Count(c => c.BatchId == id && !c.Revoked);
            return Page();
        }
    }
}
