using Microsoft.AspNetCore.Mvc.RazorPages;
using StaffNavi.Services;

namespace StaffNavi.Pages.Advance
{
    /// <summary>
    /// サービス運営画面のイメージ。前払いの仕組みを他社にもサービスとして提供する場合に、運営者（サービス提供者）が見る画面。
    /// 自社の数値は実際のデモデータから、他の導入企業は架空の固定値で表示する。
    /// </summary>
    public class ServiceModel(AdvanceService advance, LaborRepository repo) : PageModel
    {
        public record Tenant(string Name, string Industry, bool IsSelf, int Staff, int Users, int Requests, long Amount, string Since);

        /// <summary>月額基本料（1社あたり）</summary>
        public const long BaseFee = 30000;

        public List<Tenant> Tenants { get; private set; } = [];
        public AdvanceDashboard Self { get; private set; } = new();
        public long FeePerRequest { get; private set; }

        public long TotalAmount => Tenants.Sum(t => t.Amount);
        public int TotalRequests => Tenants.Sum(t => t.Requests);
        public int TotalStaff => Tenants.Sum(t => t.Staff);
        public int TotalUsers => Tenants.Sum(t => t.Users);
        public long Revenue => Tenants.Count * BaseFee + TotalRequests * FeePerRequest;

        public void OnGet()
        {
            Self = advance.GetDashboard(Clock.Now);
            FeePerRequest = Self.Settings.Fee;
            Tenants =
            [
                new(repo.GetSettings().CompanyName, "SS運営受託", true, Self.EligibleCount, Self.UserCount, Self.RequestCount, Self.MonthTotal, "2026/12（予定）"),
                new("株式会社サンプル警備保障", "警備", false, 420, 151, 388, 4_126_000, "2027/02（想定）"),
                new("みらい物流株式会社", "物流・倉庫", false, 860, 274, 702, 7_985_000, "2027/04（想定）"),
                new("株式会社クリーンライフ", "ビルメンテナンス", false, 230, 96, 241, 2_312_000, "2027/06（想定）"),
            ];
        }
    }
}
