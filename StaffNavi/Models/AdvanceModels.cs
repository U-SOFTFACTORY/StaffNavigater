namespace StaffNavi.Models
{
    /// <summary>前払い申請の状態</summary>
    public enum AdvanceStatus
    {
        /// <summary>承認待ち（自動承認の上限額を超える申請）</summary>
        Requested,
        /// <summary>承認済み・振込待ち</summary>
        Approved,
        /// <summary>振込済み</summary>
        Transferred,
        /// <summary>却下</summary>
        Rejected,
    }

    public static class AdvanceStatusExtensions
    {
        public static string Label(this AdvanceStatus status) => status switch
        {
            AdvanceStatus.Requested => "承認待ち",
            AdvanceStatus.Approved => "振込待ち",
            AdvanceStatus.Transferred => "振込済",
            AdvanceStatus.Rejected => "却下",
            _ => "",
        };

        public static string Pill(this AdvanceStatus status) => status switch
        {
            AdvanceStatus.Requested => "pill-orange",
            AdvanceStatus.Approved => "pill-blue",
            AdvanceStatus.Transferred => "pill-green",
            _ => "pill-gray",
        };

        /// <summary>前払い可能額・給与からの差引の対象になる申請（却下以外）</summary>
        public static bool IsActive(this AdvanceStatus status) => status != AdvanceStatus.Rejected;
    }

    /// <summary>前払い申請</summary>
    public class AdvanceRequest
    {
        public string Id { get; set; } = "";
        public string EmployeeNo { get; set; } = "";
        public string StaffName { get; set; } = "";
        public string Workplace { get; set; } = "";
        /// <summary>対象の就業月（yyyy-MM）。この月の給与から差し引く</summary>
        public string WorkMonth { get; set; } = "";
        /// <summary>申請額（給与から差し引く額）</summary>
        public long Amount { get; set; }
        /// <summary>振込手数料（スタッフ負担の場合は申請額から差し引く）</summary>
        public long Fee { get; set; }
        /// <summary>振込額</summary>
        public long TransferAmount { get; set; }
        /// <summary>申請時点の前払い可能額（審査の記録）</summary>
        public long AvailableAtRequest { get; set; }
        public DateTimeOffset RequestedAt { get; set; }
        /// <summary>振込予定日</summary>
        public DateOnly ScheduledDate { get; set; }
        public AdvanceStatus Status { get; set; }
        public bool AutoApproved { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTimeOffset? ApprovedAt { get; set; }
        public DateTimeOffset? TransferredAt { get; set; }
        public string? RejectReason { get; set; }
        /// <summary>申請元（スマホアプリ／管理画面）</summary>
        public string Channel { get; set; } = "スマホアプリ";
        public DateTimeOffset UpdatedAt { get; set; }
    }

    /// <summary>前払いの条件（導入企業ごとの設定）</summary>
    public class AdvanceSettings
    {
        /// <summary>働いた分（見込の総支給額）のうち前払いできる割合</summary>
        public decimal LimitRate { get; set; } = 0.7m;
        /// <summary>1回の最低申請額</summary>
        public long MinAmount { get; set; } = 3000;
        /// <summary>申請単位</summary>
        public long Unit { get; set; } = 1000;
        /// <summary>1か月の申請回数の上限</summary>
        public int MaxRequestsPerMonth { get; set; } = 5;
        /// <summary>振込手数料（1回あたり・税込）</summary>
        public long Fee { get; set; } = 330;
        /// <summary>手数料をスタッフが負担する（false：会社負担）</summary>
        public bool FeePaidByStaff { get; set; } = true;
        /// <summary>この金額以下の申請は自動承認</summary>
        public long AutoApproveLimit { get; set; } = 20000;
        /// <summary>当日振込の締め時刻（時）。これより後の申請は翌営業日の振込</summary>
        public int CutoffHour { get; set; } = 15;
    }

    /// <summary>スタッフに表示する振込先口座（デモ用の架空の口座）</summary>
    public record BankAccount(string BankCode, string BankName, string BankKana, string BranchCode, string BranchName, string BranchKana, string Number)
    {
        public string Masked => $"{BankName} {BranchName} 普通 ****{Number[^3..]}";
    }
}
