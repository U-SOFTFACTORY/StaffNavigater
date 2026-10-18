using System.Text;

namespace StaffNavi.Services
{
    public enum LedgerValueKind
    {
        Days,
        DayPair,
        Time,
        Yen,
        Count,
    }

    /// <summary>賃金台帳の1行（Group が変わる位置に区切り線を引く）</summary>
    public record LedgerItem(string Label, LedgerValueKind Kind, Func<StaffMonth, long> Value, int Group, bool Strong = false);

    /// <summary>
    /// スタッフナビゲーターの帳票・CSV形式（スタッフナビゲーター マニュアル 8章 給与管理 に準拠）
    /// </summary>
    public static class StaffNavigatorFormat
    {
        /// <summary>賃金台帳の記載項目と並び順（マニュアル P594「賃金台帳」サンプル）</summary>
        public static readonly LedgerItem[] LedgerItems =
        [
            new("出勤日数", LedgerValueKind.Days, s => s.Summary.WorkDays, 1),
            new("欠勤日数", LedgerValueKind.Days, s => s.Summary.AbsenceDays, 1),
            new("休出日数", LedgerValueKind.Days, s => s.Summary.HolidayWorkDays, 1),
            new("有給日数", LedgerValueKind.Days, s => s.Summary.PaidLeaveDays, 1),
            new("遅早日数", LedgerValueKind.Days, s => 0, 1),
            new("特休／休２", LedgerValueKind.DayPair, s => 0, 1),
            new("休３／休４", LedgerValueKind.DayPair, s => 0, 1),
            new("基本時間", LedgerValueKind.Time, s => s.Summary.BasicMinutes, 1),
            new("普通残時間", LedgerValueKind.Time, s => s.Summary.OvertimeMinutes, 1),
            new("深夜時間", LedgerValueKind.Time, s => s.Summary.NightMinutes, 1),
            new("深夜残時間", LedgerValueKind.Time, s => s.Summary.NightOvertimeMinutes, 1),
            new("休出時間", LedgerValueKind.Time, s => s.Summary.HolidayMinutes, 1),
            new("その他時間外", LedgerValueKind.Time, s => 0, 1),
            new("有給時間", LedgerValueKind.Time, s => s.Summary.PaidLeaveMinutes, 1),
            new("遅早", LedgerValueKind.Time, s => 0, 1),

            new("基本", LedgerValueKind.Yen, s => s.Wage.BasicPay, 2),
            new("普通残", LedgerValueKind.Yen, s => s.Wage.OvertimePay, 2),
            new("深夜", LedgerValueKind.Yen, s => s.Wage.NightPay, 2),
            new("深夜残", LedgerValueKind.Yen, s => s.Wage.NightOvertimePay, 2),
            new("休出", LedgerValueKind.Yen, s => s.Wage.HolidayPay, 2),
            new("その他時間外手当", LedgerValueKind.Yen, s => 0, 2),
            new("有給手当", LedgerValueKind.Yen, s => s.Wage.PaidLeavePay, 2),
            new("交通費非課税", LedgerValueKind.Yen, s => s.Wage.CommuteNonTaxable, 2),
            new("交通費課税", LedgerValueKind.Yen, s => 0, 2),
            new("遅早控除", LedgerValueKind.Yen, s => 0, 2),
            new("欠勤控除", LedgerValueKind.Yen, s => 0, 2),
            new("休暇手当", LedgerValueKind.Yen, s => 0, 2),
            new("休暇控除", LedgerValueKind.Yen, s => 0, 2),
            new("その他課税", LedgerValueKind.Yen, s => 0, 2),
            new("その他非課税", LedgerValueKind.Yen, s => 0, 2),

            new("課税支給額", LedgerValueKind.Yen, s => s.Wage.TaxablePay, 3),
            new("非課税支給額", LedgerValueKind.Yen, s => s.Wage.NonTaxablePay, 3),
            new("総支給額", LedgerValueKind.Yen, s => s.Wage.GrossPay, 3, true),
            new("雇用保険対象額", LedgerValueKind.Yen, s => s.Wage.EmploymentInsuranceBase, 3),

            new("扶養家族人数", LedgerValueKind.Count, s => s.Wage.Dependents, 4),
            new("健康保険", LedgerValueKind.Yen, s => s.Wage.HealthInsurance, 4),
            new("介護保険", LedgerValueKind.Yen, s => s.Wage.CareInsurance, 4),
            new("厚生年金", LedgerValueKind.Yen, s => s.Wage.Pension, 4),
            new("厚生年金基金", LedgerValueKind.Yen, s => 0, 4),
            new("雇用保険", LedgerValueKind.Yen, s => s.Wage.EmploymentInsurance, 4),
            new("社保合計", LedgerValueKind.Yen, s => s.Wage.SocialInsuranceTotal, 4),

            new("課税対象額", LedgerValueKind.Yen, s => s.Wage.TaxableAmount, 5),

            new("所得税", LedgerValueKind.Yen, s => s.Wage.IncomeTax, 6),
            new("年調過不足額", LedgerValueKind.Yen, s => 0, 6),
            new("住民税", LedgerValueKind.Yen, s => s.Wage.ResidentTax, 6),
            new("控除", LedgerValueKind.Yen, s => 0, 6),

            new("総控除額", LedgerValueKind.Yen, s => s.Wage.TotalDeduction, 7, true),

            new("差引支給額", LedgerValueKind.Yen, s => s.Wage.NetPay, 8, true),
        ];

        public static string FormatValue(LedgerItem item, long value) => item.Kind switch
        {
            LedgerValueKind.DayPair => $"{value}/0",
            LedgerValueKind.Time => $"{value / 60}:{value % 60:00}",
            LedgerValueKind.Yen => value.ToString("#,0"),
            _ => value.ToString(),
        };

        /// <summary>給与計（扶養家族人数は合計せず最新の値）</summary>
        public static long Total(LedgerItem item, IEnumerable<StaffMonth> months)
        {
            var list = months.ToList();
            return item.Kind == LedgerValueKind.Count
                ? list.Select(item.Value).LastOrDefault()
                : list.Sum(item.Value);
        }

        /// <summary>賃金台帳CSVファイル出力形式（マニュアル P596～597）。スタッフ種別・前払金はオプション項目のため出力しない。</summary>
        public static readonly string[] LedgerCsvHeaders =
        [
            "スタッフ番号", "スタッフ氏名", "性別", "種別", "対象年", "対象月", "賞与対象回", "支払日", "対象期間開始日", "対象期間終了日",
            "出勤日数", "欠勤日数", "休出日数", "有給日数", "遅早日数", "特別休暇日数", "休暇２日数", "休暇３日数", "休暇４日数",
            "基本時間", "普通残時間", "深夜時間", "深夜残時間", "休出時間", "その他時間外", "有給時間", "遅早時間",
            "基本", "普通残", "深夜", "深夜残", "休出", "その他時間外手当", "有給手当", "交通費非課税", "交通費課税",
            "遅早控除", "欠勤控除", "休暇手当", "休暇控除", "その他課税", "その他非課税", "課税支給額", "非課税支給額", "総支給額",
            "雇用保険対象額", "扶養家族人数", "健康保険", "介護保険", "厚生年金", "厚生年金基金", "雇用保険", "社保合計",
            "課税対象額", "所得税", "年調過不足額", "住民税", "控除", "総控除額", "差引支給額",
        ];

        public static object?[] LedgerCsvRow(MonthView view, StaffMonth s)
        {
            var sum = s.Summary;
            var w = s.Wage;
            return
            [
                s.Staff.EmployeeNo, s.Staff.Name, s.Staff.Gender, "給与", view.FirstDay.Year, view.FirstDay.Month, "",
                view.PayDate.ToString("yyyy/MM/dd"), view.FirstDay.ToString("yyyy/MM/dd"), view.LastDay.ToString("yyyy/MM/dd"),
                sum.WorkDays, sum.AbsenceDays, sum.HolidayWorkDays, sum.PaidLeaveDays, 0, 0, 0, 0, 0,
                Time(sum.BasicMinutes), Time(sum.OvertimeMinutes), Time(sum.NightMinutes), Time(sum.NightOvertimeMinutes),
                Time(sum.HolidayMinutes), Time(0), Time(sum.PaidLeaveMinutes), Time(0),
                w.BasicPay, w.OvertimePay, w.NightPay, w.NightOvertimePay, w.HolidayPay, 0, w.PaidLeavePay, w.CommuteNonTaxable, 0,
                0, 0, 0, 0, 0, 0, w.TaxablePay, w.NonTaxablePay, w.GrossPay,
                w.EmploymentInsuranceBase, w.Dependents, w.HealthInsurance, w.CareInsurance, w.Pension, 0, w.EmploymentInsurance, w.SocialInsuranceTotal,
                w.TaxableAmount, w.IncomeTax, 0, w.ResidentTax, 0, w.TotalDeduction, w.NetPay,
            ];
        }

        /// <summary>
        /// 勤怠データCSVファイル取込形式（マニュアル P614～615）。
        /// スタッフナビゲーターの「給与管理｜勤怠データCSVファイル取込」で、補正後の勤怠を取り込める。
        /// </summary>
        public static readonly string[] AttendanceCsvHeaders =
        [
            "スタッフ番号", "受注番号", "締日", "出勤日数", "欠勤日数", "休出日数", "遅早日数", "有給日数",
            "基本時間", "普通残時間", "深夜時間", "深夜残時間", "休出時間", "時間外１", "時間外２", "時間外３", "時間外４", "遅早時間", "有給時間",
            "45H超過－基本", "45H超過－普通残", "45H超過－深夜", "45H超過－深夜残", "45H超過－休出",
            "45H超過－時間外１", "45H超過－時間外２", "45H超過－時間外３", "45H超過－時間外４",
            "60H超過－基本", "60H超過－普通残", "60H超過－深夜", "60H超過－深夜残", "60H超過－休出",
            "60H超過－時間外１", "60H超過－時間外２", "60H超過－時間外３", "60H超過－時間外４",
            "休暇区分No.1日数", "休暇区分No.2日数", "休暇区分No.3日数", "休暇区分No.4日数",
        ];

        public static object?[] AttendanceCsvRow(MonthView view, StaffMonth s)
        {
            var sum = s.Summary;
            // 1か月の時間外が45時間・60時間を超えた分（割増単価の加算用）
            var over45 = Math.Clamp(sum.TotalOvertimeMinutes - 45 * 60, 0, 15 * 60);
            var over60 = Math.Max(0, sum.TotalOvertimeMinutes - 60 * 60);
            var zero = Time(0);
            return
            [
                s.Staff.EmployeeNo, 0, view.CloseDate.ToString("yyyy/MM/dd"),
                sum.WorkDays, sum.AbsenceDays, sum.HolidayWorkDays, 0, sum.PaidLeaveDays,
                Time(sum.BasicMinutes), Time(sum.OvertimeMinutes), Time(sum.NightMinutes), Time(sum.NightOvertimeMinutes), Time(sum.HolidayMinutes),
                zero, zero, zero, zero, zero, Time(sum.PaidLeaveMinutes),
                zero, Time(over45), zero, zero, zero, zero, zero, zero, zero,
                zero, Time(over60), zero, zero, zero, zero, zero, zero, zero,
                0, 0, 0, 0,
            ];
        }

        /// <summary>“時間:分”形式（例：01:30）</summary>
        public static string Time(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        /// <summary>スタッフナビゲーターに合わせ、Shift_JISで出力する</summary>
        public static byte[] ToShiftJis(string csv) => Encoding.GetEncoding(932).GetBytes(csv);
    }
}
