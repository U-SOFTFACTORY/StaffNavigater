using StaffNavi.Models;
using StaffNavi.Services;

namespace StaffNavi.Pages.App
{
    public class HistoryModel(LaborRepository repo) : AppPageModel
    {
        public List<AdvanceRequest> Requests { get; private set; } = [];

        public void OnGet() =>
            Requests = repo.GetAdvances().Where(r => r.EmployeeNo == EmployeeNo && r.RequestedAt <= Clock.Now).OrderByDescending(r => r.RequestedAt).ToList();
    }
}
