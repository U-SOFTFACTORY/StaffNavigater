using System.Text.Json.Serialization;

namespace StaffNavi.Models
{
    /// <summary>勤務区分</summary>
    public enum DayKind
    {
        Work,
        HolidayWork,
        PaidLeave,
        DayOff,
        Absence,
    }

    /// <summary>取込CSVの種類</summary>
    public enum ImportKind
    {
        Attendance,
        Staff,
    }

    public static class DayKindExtensions
    {
        public static readonly DayKind[] All = [DayKind.Work, DayKind.HolidayWork, DayKind.PaidLeave, DayKind.DayOff, DayKind.Absence];

        public static string Label(this DayKind kind) => kind switch
        {
            DayKind.Work => "出勤",
            DayKind.HolidayWork => "休日出勤",
            DayKind.PaidLeave => "有給",
            DayKind.DayOff => "公休",
            DayKind.Absence => "欠勤",
            _ => "",
        };

        public static bool IsWorking(this DayKind kind) => kind is DayKind.Work or DayKind.HolidayWork;

        public static bool TryParse(string? text, out DayKind kind)
        {
            switch (text?.Trim())
            {
                case "出勤": kind = DayKind.Work; return true;
                case "休日出勤": kind = DayKind.HolidayWork; return true;
                case "有給": case "有給休暇": case "有休": kind = DayKind.PaidLeave; return true;
                case "公休": case "休日": kind = DayKind.DayOff; return true;
                case "欠勤": kind = DayKind.Absence; return true;
                default: kind = DayKind.Work; return false;
            }
        }
    }

    public static class ImportKindExtensions
    {
        public static string Label(this ImportKind kind) => kind == ImportKind.Attendance ? "勤怠実績" : "スタッフ情報";
    }

    /// <summary>スタッフ情報（スタッフナビゲーターから出力されたCSVの内容）</summary>
    public class StaffMember
    {
        /// <summary>スタッフ番号（スタッフナビゲーターと同じく1～8桁の数字）</summary>
        public string EmployeeNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kana { get; set; } = "";
        public string Gender { get; set; } = "";
        public DateOnly? BirthDate { get; set; }
        public string Site { get; set; } = "";
        public string EmploymentType { get; set; } = "";
        public DateOnly HireDate { get; set; }
        public int HourlyWage { get; set; }
        public int CommuteAllowancePerDay { get; set; }
        public int ResidentTax { get; set; }
        public int Dependents { get; set; }

        /// <summary>指定日時点の年齢</summary>
        public int? AgeAt(DateOnly date)
        {
            if (BirthDate is not { } birth)
            {
                return null;
            }
            var age = date.Year - birth.Year;
            return date < birth.AddYears(age) ? age - 1 : age;
        }
    }

    /// <summary>1日分の勤務内容（補正前後の比較単位）</summary>
    public record AttendanceValues(DayKind Kind, TimeOnly? Start, TimeOnly? End, int BreakMinutes)
    {
        public string Describe()
        {
            if (!Kind.IsWorking())
            {
                return Kind.Label();
            }
            var start = Start?.ToString("HH:mm") ?? "--:--";
            var end = End?.ToString("HH:mm") ?? "--:--";
            return $"{Kind.Label()} {start}～{end} 休憩{BreakMinutes}分";
        }
    }

    /// <summary>取込CSVの1行（原本データ。取込後は変更しない）</summary>
    public class AttendanceRecord
    {
        public int LineNo { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Site { get; set; } = "";
        public DateOnly Date { get; set; }
        public DayKind Kind { get; set; }
        public TimeOnly? Start { get; set; }
        public TimeOnly? End { get; set; }
        public int BreakMinutes { get; set; }
        public string? Note { get; set; }

        [JsonIgnore]
        public AttendanceValues Values => new(Kind, Start, End, BreakMinutes);
    }

    public class ImportIssue
    {
        public const string Error = "エラー";
        public const string Warning = "警告";

        public int LineNo { get; set; }
        public string Level { get; set; } = Warning;
        public string? EmployeeNo { get; set; }
        public DateOnly? Date { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>取込履歴</summary>
    public class ImportBatch
    {
        public string Id { get; set; } = "";
        public ImportKind Kind { get; set; }
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Encoding { get; set; } = "";
        public DateTimeOffset ImportedAt { get; set; }
        public string ImportedBy { get; set; } = "";
        public string? TargetMonth { get; set; }
        public int RowCount { get; set; }
        public int WarningCount { get; set; }
    }

    public class BatchData
    {
        public List<AttendanceRecord> Records { get; set; } = [];
        public List<StaffMember> Staff { get; set; } = [];
        public List<ImportIssue> Issues { get; set; } = [];
    }

    /// <summary>取込確認待ちのCSV</summary>
    public class PendingImport
    {
        public string Token { get; set; } = "";
        public ImportKind? Kind { get; set; }
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public string Encoding { get; set; } = "";
        public string? TargetMonth { get; set; }
        public string UploadedBy { get; set; } = "";
        public DateTimeOffset UploadedAt { get; set; }
        public List<string> Notices { get; set; } = [];
        public BatchData Data { get; set; } = new();

        [JsonIgnore]
        public int ErrorCount => Data.Issues.Count(i => i.Level == ImportIssue.Error);

        [JsonIgnore]
        public int WarningCount => Data.Issues.Count(i => i.Level == ImportIssue.Warning);

        [JsonIgnore]
        public bool CanCommit => Kind != null && ErrorCount == 0;
    }

    /// <summary>労務資料側での補正記録（取込データ・スタッフナビゲーターの正データは変更しない）</summary>
    public class Correction
    {
        public string Id { get; set; } = "";
        public string BatchId { get; set; } = "";
        public string Month { get; set; } = "";
        public string EmployeeNo { get; set; } = "";
        public string StaffName { get; set; } = "";
        public DateOnly Date { get; set; }
        public AttendanceValues? Before { get; set; }
        public AttendanceValues After { get; set; } = new(DayKind.Work, null, null, 0);
        public string Reason { get; set; } = "";
        public string? Evidence { get; set; }
        public string CorrectedBy { get; set; } = "";
        public DateTimeOffset CorrectedAt { get; set; }
        public bool Revoked { get; set; }
        public string? RevokedBy { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? RevokeReason { get; set; }
    }

    public class OperationLog
    {
        public DateTimeOffset At { get; set; }
        public string User { get; set; } = "";
        public string Category { get; set; } = "";
        public string Message { get; set; } = "";
    }

    /// <summary>計算設定（デモ用の簡易計算）</summary>
    public class LaborSettings
    {
        public string CompanyName { get; set; } = "株式会社サンプルエナジー";
        /// <summary>給与の支払日（締日は末日、支払いは翌月のこの日）</summary>
        public int PayDayOfMonth { get; set; } = 25;
        public int DailyStandardMinutes { get; set; } = 480;
        public int PaidLeaveMinutes { get; set; } = 480;
        public decimal OvertimeRate { get; set; } = 1.25m;
        public decimal HolidayRate { get; set; } = 1.35m;
        public decimal NightRate { get; set; } = 0.25m;
        public decimal HealthInsuranceRate { get; set; } = 0.0499m;
        /// <summary>介護保険料率（40歳以上65歳未満が対象）</summary>
        public decimal CareInsuranceRate { get; set; } = 0.0080m;
        public decimal PensionRate { get; set; } = 0.0915m;
        public decimal EmploymentInsuranceRate { get; set; } = 0.0055m;
        public decimal IncomeTaxRate { get; set; } = 0.03063m;
    }
}
