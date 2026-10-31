using System.Text.Json.Serialization;
using StaffNavi.Services;

namespace StaffNavi.Models
{
    /// <summary>
    /// 出欠（スタッフナビゲーターのタイムシートの「出欠」欄。HolidayWork・NoWork は本システムの補正用）
    /// </summary>
    public enum DayKind
    {
        Work,
        Absence,
        PaidLeave,
        HalfPaidLeave,
        SpecialLeave,
        HourlyPaidLeave,
        Leave2,
        Leave3,
        Leave4,
        HolidayWork,
        NoWork,
    }

    /// <summary>取込CSVの種類</summary>
    public enum ImportKind
    {
        Attendance,
        Staff,
        Payroll,
    }

    public static class DayKindExtensions
    {
        /// <summary>補正画面で選べる出欠</summary>
        public static readonly DayKind[] Selectable = [DayKind.Work, DayKind.HolidayWork, DayKind.HalfPaidLeave, DayKind.PaidLeave, DayKind.SpecialLeave, DayKind.Absence, DayKind.NoWork];

        /// <summary>
        /// タイムシートCSVの「出欠状況」コード。勤務表（実績）の出欠の選択肢の並び（出勤・欠勤・有給・半給・特休・時有給・休２・休３・休４）を
        /// 0から順に割り当てた想定（0＝出勤は実データで確認済み。1以降は要確認）。
        /// </summary>
        private static readonly DayKind[] StatusCodes =
            [DayKind.Work, DayKind.Absence, DayKind.PaidLeave, DayKind.HalfPaidLeave, DayKind.SpecialLeave, DayKind.HourlyPaidLeave, DayKind.Leave2, DayKind.Leave3, DayKind.Leave4];

        public static string Label(this DayKind kind) => kind switch
        {
            DayKind.Work => "出勤",
            DayKind.Absence => "欠勤",
            DayKind.PaidLeave => "有給",
            DayKind.HalfPaidLeave => "半給",
            DayKind.SpecialLeave => "特休",
            DayKind.HourlyPaidLeave => "時有給",
            DayKind.Leave2 => "休２",
            DayKind.Leave3 => "休３",
            DayKind.Leave4 => "休４",
            DayKind.HolidayWork => "休日出勤",
            DayKind.NoWork => "勤務なし",
            _ => "",
        };

        /// <summary>始業・終業の時刻を持つ出欠（半給は半日勤務がある場合に時刻を入れる）</summary>
        public static bool HasTimes(this DayKind kind) => kind is DayKind.Work or DayKind.HolidayWork or DayKind.HalfPaidLeave or DayKind.HourlyPaidLeave;

        /// <summary>時刻の入力が必須の出欠</summary>
        public static bool RequiresTimes(this DayKind kind) => kind is DayKind.Work or DayKind.HolidayWork;

        public static bool TryParseStatus(string? text, out DayKind kind)
        {
            var value = text?.Trim() ?? "";
            if (int.TryParse(value, out var code) && code >= 0 && code < StatusCodes.Length)
            {
                kind = StatusCodes[code];
                return true;
            }
            var found = StatusCodes.FirstOrDefault(k => k.Label() == value, (DayKind)(-1));
            kind = found == (DayKind)(-1) ? DayKind.Work : found;
            return found != (DayKind)(-1);
        }

        public static int StatusCode(this DayKind kind) => Math.Max(0, Array.IndexOf(StatusCodes, kind == DayKind.HolidayWork ? DayKind.Work : kind));
    }

    public static class ImportKindExtensions
    {
        public static string Label(this ImportKind kind) => kind switch
        {
            ImportKind.Attendance => "タイムシート",
            ImportKind.Staff => "スタッフ情報",
            _ => "給与計算書",
        };

        public static string Pill(this ImportKind kind) => kind switch
        {
            ImportKind.Attendance => "pill-blue",
            ImportKind.Payroll => "pill-green",
            _ => "pill-gray",
        };
    }

    /// <summary>スタッフ情報（スタッフナビゲーターの「スタッフ全データ」CSVのうち、労務資料に使う項目）</summary>
    public class StaffMember
    {
        /// <summary>スタッフ番号（スタッフナビゲーターと同じく1～8桁の数字）</summary>
        public string EmployeeNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Kana { get; set; } = "";
        public string Gender { get; set; } = "";
        public DateOnly? BirthDate { get; set; }
        /// <summary>ｽﾀｯﾌ区分（例：派・ｱﾙ《有雇･社保無》）</summary>
        public string Category { get; set; } = "";
        /// <summary>雇用形態（一般／短時間労働者 など）</summary>
        public string EmploymentForm { get; set; } = "";
        public DateOnly? HireDate { get; set; }
        public bool Retired { get; set; }
        public DateOnly? RetireDate { get; set; }
        public bool HealthInsured { get; set; }
        public int HealthGrade { get; set; }
        public bool PensionInsured { get; set; }
        public int PensionGrade { get; set; }
        public bool CareInsured { get; set; }
        public bool EmploymentInsured { get; set; }
        /// <summary>税区分（甲欄／乙欄）</summary>
        public string TaxCategory { get; set; } = "";
        public int Dependents { get; set; }
        /// <summary>住民税（7月以降の月額）</summary>
        public int ResidentTax { get; set; }
        /// <summary>１日あたり有給時間</summary>
        public decimal PaidLeaveHoursPerDay { get; set; } = 8;

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

    /// <summary>
    /// 受注（スタッフナビゲーターの受注。SSの勤務帯ごとに受注が分かれ、タイムシートは受注ごとに作成される）
    /// </summary>
    public class WorkOrder
    {
        /// <summary>受注番号（8桁）</summary>
        public string OrderNo { get; set; } = "";
        /// <summary>受注名称</summary>
        public string Name { get; set; } = "";
        /// <summary>請負先</summary>
        public string Client { get; set; } = "";
        /// <summary>就業先（SS）</summary>
        public string Workplace { get; set; } = "";
        public string Address { get; set; } = "";
        public string Area { get; set; } = "";
        public string JobDescription { get; set; } = "";
        /// <summary>業務内容２（勤務帯：A勤・B勤・夜間 など）</summary>
        public string ShiftName { get; set; } = "";
        /// <summary>所定の始業（分。24時以降は 24:00＋）</summary>
        public int ShiftStart { get; set; }
        /// <summary>所定の終業（分。翌日にまたがる場合は 1440 を超える。例：32:00＝1920）</summary>
        public int ShiftEnd { get; set; }
        /// <summary>所定労働時間（分）。これを超えた分を普通残・深夜残とする</summary>
        public int StandardMinutes { get; set; } = 480;
        /// <summary>支払単価（基本・時給）</summary>
        public int HourlyWage { get; set; }
        /// <summary>交通費（日額・非課税）</summary>
        public int CommutePerDay { get; set; }

        [JsonIgnore]
        public string ShiftLabel => $"{Fmt.Clock(ShiftStart)}-{Fmt.Clock(ShiftEnd)}";
    }

    /// <summary>
    /// 1日分の勤務内容（補正前後の比較単位）。時刻は0:00からの分で持ち、日をまたぐ終業は 24:00 以降（例：32:00）で表す
    /// </summary>
    public record AttendanceValues(DayKind Kind, int? Start, int? End, int BreakMinutes)
    {
        public string Describe()
        {
            if (!Kind.HasTimes() || (Kind == DayKind.HalfPaidLeave && Start == null && End == null))
            {
                return Kind.Label();
            }
            var start = Start != null ? Fmt.Clock(Start) : "--:--";
            var end = End != null ? Fmt.Clock(End) : "--:--";
            return $"{Kind.Label()} {start}-{end} 休憩{BreakMinutes}分";
        }
    }

    /// <summary>タイムシートCSVの1行（原本データ。取込後は変更しない）</summary>
    public class AttendanceRecord
    {
        public int LineNo { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string OrderNo { get; set; } = "";
        public DateOnly Date { get; set; }
        public int DataNo { get; set; } = 1;
        /// <summary>出欠状況（CSVの値そのまま）</summary>
        public string Status { get; set; } = "";
        public DayKind Kind { get; set; }
        public int? Start { get; set; }
        public int? End { get; set; }
        public int BreakMinutes { get; set; }

        [JsonIgnore]
        public AttendanceValues Values => new(Kind, Start, End, BreakMinutes);
    }

    /// <summary>給与計算書CSVの1行（スタッフナビゲーターの給与計算結果。項目名→値をそのまま保持する）</summary>
    public class PayrollRecord
    {
        public int LineNo { get; set; }
        public string EmployeeNo { get; set; } = "";
        public string Name { get; set; } = "";
        public DateOnly PayDate { get; set; }
        public Dictionary<string, string> Values { get; set; } = [];
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
        /// <summary>タイムシート：就業月、給与計算書：支給月（yyyy-MM）</summary>
        public string? TargetMonth { get; set; }
        /// <summary>タイムシートに含まれる受注番号</summary>
        public List<string> OrderNos { get; set; } = [];
        public int RowCount { get; set; }
        public int WarningCount { get; set; }
    }

    public class BatchData
    {
        public List<AttendanceRecord> Records { get; set; } = [];
        public List<StaffMember> Staff { get; set; } = [];
        public List<PayrollRecord> Payroll { get; set; } = [];
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
        public List<string> OrderNos { get; set; } = [];
        public string UploadedBy { get; set; } = "";
        public DateTimeOffset UploadedAt { get; set; }
        public List<string> Notices { get; set; } = [];
        public BatchData Data { get; set; } = new();

        [JsonIgnore]
        public int ErrorCount => Data.Issues.Count(i => i.Level == ImportIssue.Error);

        [JsonIgnore]
        public int WarningCount => Data.Issues.Count(i => i.Level == ImportIssue.Warning);

        [JsonIgnore]
        public int RowCount => Kind switch
        {
            ImportKind.Attendance => Data.Records.Count,
            ImportKind.Payroll => Data.Payroll.Count,
            _ => Data.Staff.Count,
        };

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
        public string OrderNo { get; set; } = "";
        public string OrderName { get; set; } = "";
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

    /// <summary>計算設定（給与計算書が未取込の月の試算に使う）</summary>
    public class LaborSettings
    {
        public string CompanyName { get; set; } = "株式会社サンプルエナジー";
        /// <summary>給与の支払日（締日は末日、支払いは翌月のこの日。土日の場合は直前の平日）</summary>
        public int PayDayOfMonth { get; set; } = 25;
        public decimal OvertimeRate { get; set; } = 1.25m;
        public decimal HolidayRate { get; set; } = 1.35m;
        public decimal NightRate { get; set; } = 0.25m;
        /// <summary>健康保険料率（本人負担。標準報酬月額に乗じる）</summary>
        public decimal HealthInsuranceRate { get; set; } = 0.0504m;
        /// <summary>介護保険料率（本人負担。スタッフ情報の介護保険が「対象」の人）</summary>
        public decimal CareInsuranceRate { get; set; } = 0.0081m;
        public decimal PensionRate { get; set; } = 0.0915m;
        public decimal EmploymentInsuranceRate { get; set; } = 0.005m;
        public decimal IncomeTaxRate { get; set; } = 0.03063m;
        /// <summary>乙欄の所得税率（簡易）</summary>
        public decimal IncomeTaxRateOtsu { get; set; } = 0.1021m;
    }
}
