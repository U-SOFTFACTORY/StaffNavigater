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
    public record LedgerItem(string Label, LedgerValueKind Kind, Func<LedgerMonth, decimal> Value, int Group, bool Strong = false, Func<LedgerMonth, decimal>? Second = null);

    /// <summary>
    /// スタッフナビゲーターの帳票・CSV形式（マニュアル 8章 給与管理、および先方の賃金台帳（Excel）に準拠）
    /// </summary>
    public static class StaffNavigatorFormat
    {
        /// <summary>賃金台帳の記載項目と並び順（先方の賃金台帳Excel「印刷用」シートと同じ）</summary>
        public static readonly LedgerItem[] LedgerItems =
        [
            new("出勤日数", LedgerValueKind.Days, s => s.WorkDays, 1),
            new("欠勤日数", LedgerValueKind.Days, s => s.AbsenceDays, 1),
            new("休出日数", LedgerValueKind.Days, s => s.HolidayWorkDays, 1),
            new("有給日数", LedgerValueKind.Days, s => s.PaidLeaveDays, 1),
            new("遅早日数", LedgerValueKind.Days, s => s.LateEarlyDays, 1),
            new("特休/休２", LedgerValueKind.DayPair, s => s.SpecialLeaveDays, 1, Second: s => s.Leave2Days),
            new("休３/休４", LedgerValueKind.DayPair, s => s.Leave3Days, 1, Second: s => s.Leave4Days),
            new("基本時間", LedgerValueKind.Time, s => s.BasicMinutes, 1),
            new("普通残時間", LedgerValueKind.Time, s => s.OvertimeMinutes, 1),
            new("深夜時間", LedgerValueKind.Time, s => s.NightMinutes, 1),
            new("深夜残時間", LedgerValueKind.Time, s => s.NightOvertimeMinutes, 1),
            new("休出時間", LedgerValueKind.Time, s => s.HolidayMinutes, 1),
            new("その他時間外", LedgerValueKind.Time, s => s.OtherOvertimeMinutes, 1),
            new("有給時間", LedgerValueKind.Time, s => s.PaidLeaveMinutes, 1),
            new("遅早", LedgerValueKind.Time, s => s.LateEarlyMinutes, 1),

            new("基本", LedgerValueKind.Yen, s => s.BasicPay, 2),
            new("普通残", LedgerValueKind.Yen, s => s.OvertimePay, 2),
            new("深夜", LedgerValueKind.Yen, s => s.NightPay, 2),
            new("深夜残", LedgerValueKind.Yen, s => s.NightOvertimePay, 2),
            new("休出", LedgerValueKind.Yen, s => s.HolidayPay, 2),
            new("その他時間外手当", LedgerValueKind.Yen, s => s.OtherOvertimePay, 2),
            new("有給手当", LedgerValueKind.Yen, s => s.PaidLeavePay, 2),
            new("交通費課税", LedgerValueKind.Yen, s => s.CommuteTaxable, 2),
            new("交通費非課税", LedgerValueKind.Yen, s => s.CommuteNonTaxable, 2),
            new("遅早控除", LedgerValueKind.Yen, s => s.LateEarlyDeduction, 2),
            new("欠勤控除", LedgerValueKind.Yen, s => s.AbsenceDeduction, 2),
            new("休暇手当", LedgerValueKind.Yen, s => s.LeavePay, 2),
            new("休暇控除", LedgerValueKind.Yen, s => s.LeaveDeduction, 2),
            new("その他課税", LedgerValueKind.Yen, s => s.OtherTaxable, 2),
            new("その他非課税", LedgerValueKind.Yen, s => s.OtherNonTaxable, 2),

            new("課税支給額", LedgerValueKind.Yen, s => s.TaxablePay, 3),
            new("非課税支給額", LedgerValueKind.Yen, s => s.NonTaxablePay, 3),
            new("総支給額", LedgerValueKind.Yen, s => s.GrossPay, 3, true),
            new("雇用保険対象額", LedgerValueKind.Yen, s => s.EmploymentInsuranceBase, 3),

            new("扶養家族人数", LedgerValueKind.Count, s => s.Dependents, 4),
            new("健康保険", LedgerValueKind.Yen, s => s.HealthInsurance, 4),
            new("介護保険", LedgerValueKind.Yen, s => s.CareInsurance, 4),
            new("厚生年金", LedgerValueKind.Yen, s => s.Pension, 4),
            new("厚生年金基金", LedgerValueKind.Yen, s => s.PensionFund, 4),
            new("雇用保険", LedgerValueKind.Yen, s => s.EmploymentInsurance, 4),
            new("社保合計", LedgerValueKind.Yen, s => s.SocialInsuranceTotal, 4),

            new("課税対象額", LedgerValueKind.Yen, s => s.TaxableAmount, 5),

            new("所得税", LedgerValueKind.Yen, s => s.IncomeTax, 6),
            new("年調過不足額", LedgerValueKind.Yen, s => s.YearEndAdjustment, 6),
            new("住民税", LedgerValueKind.Yen, s => s.ResidentTax, 6),
            new("控除", LedgerValueKind.Yen, s => s.OtherDeduction, 6),

            new("総控除額", LedgerValueKind.Yen, s => s.TotalDeduction, 7, true),

            new("差引支給額", LedgerValueKind.Yen, s => s.NetPay, 8, true),

            new("速払金", LedgerValueKind.Yen, s => s.AdvancePayment, 9),
        ];

        public static string FormatValue(LedgerItem item, LedgerMonth month) =>
            item.Kind == LedgerValueKind.DayPair
                ? $"{Fmt.Days(item.Value(month))}/{Fmt.Days(item.Second!(month))}"
                : FormatValue(item, item.Value(month));

        public static string FormatValue(LedgerItem item, decimal value) => item.Kind switch
        {
            LedgerValueKind.Time => $"{(int)value / 60}:{(int)value % 60:00}",
            LedgerValueKind.Yen => value.ToString("#,0"),
            LedgerValueKind.Days => Fmt.Days(value),
            _ => value.ToString("0"),
        };

        /// <summary>給与計（扶養家族人数は合計せず最新の値）</summary>
        public static string Total(LedgerItem item, IEnumerable<LedgerMonth> months)
        {
            var list = months.ToList();
            if (item.Kind == LedgerValueKind.DayPair)
            {
                return $"{Fmt.Days(list.Sum(item.Value))}/{Fmt.Days(list.Sum(item.Second!))}";
            }
            return FormatValue(item, item.Kind == LedgerValueKind.Count ? list.Select(item.Value).LastOrDefault() : list.Sum(item.Value));
        }

        /// <summary>賃金台帳CSV（先方の賃金台帳Excel「csv」シートと同じ61項目。マニュアル P596～597 の形式＋速払金）</summary>
        public static readonly string[] LedgerCsvHeaders =
        [
            "スタッフ番号", "スタッフ氏名", "性別", "種別", "対象年", "対象月", "賞与対象回", "支払日", "対象期間開始日", "対象期間終了日",
            "出勤日数", "欠勤日数", "休出日数", "有給日数", "遅早日数", "特別休暇日数", "休暇２日数", "休暇３日数", "休暇４日数",
            "基本時間", "普通残時間", "深夜時間", "深夜残時間", "休出時間", "その他時間外", "有給時間", "遅早時間",
            "基本", "普通残", "深夜", "深夜残", "休出", "その他時間外手当", "有給手当", "交通費非課税", "交通費課税",
            "遅早控除", "欠勤控除", "休暇手当", "休暇控除", "その他課税", "その他非課税", "課税支給額", "非課税支給額", "総支給額",
            "雇用保険対象額", "扶養家族人数", "健康保険", "介護保険", "厚生年金", "厚生年金基金", "雇用保険", "社保合計",
            "課税対象額", "所得税", "年調過不足額", "住民税", "控除", "総控除額", "差引支給額", "速払金",
        ];

        public static object?[] LedgerCsvRow(LedgerMonth s) =>
        [
            s.EmployeeNo, s.Name, s.Gender, "給与", s.PayDate.Year, s.PayDate.Month, "",
            s.PayDate.ToString("yyyy/MM/dd"), s.PeriodStart.ToString("yyyy/MM/dd"), s.PeriodEnd.ToString("yyyy/MM/dd"),
            Fmt.Days(s.WorkDays), Fmt.Days(s.AbsenceDays), Fmt.Days(s.HolidayWorkDays), Fmt.Days(s.PaidLeaveDays), Fmt.Days(s.LateEarlyDays),
            Fmt.Days(s.SpecialLeaveDays), Fmt.Days(s.Leave2Days), Fmt.Days(s.Leave3Days), Fmt.Days(s.Leave4Days),
            Time(s.BasicMinutes), Time(s.OvertimeMinutes), Time(s.NightMinutes), Time(s.NightOvertimeMinutes),
            Time(s.HolidayMinutes), Time(s.OtherOvertimeMinutes), Time(s.PaidLeaveMinutes), Time(s.LateEarlyMinutes),
            s.BasicPay, s.OvertimePay, s.NightPay, s.NightOvertimePay, s.HolidayPay, s.OtherOvertimePay, s.PaidLeavePay, s.CommuteNonTaxable, s.CommuteTaxable,
            s.LateEarlyDeduction, s.AbsenceDeduction, s.LeavePay, s.LeaveDeduction, s.OtherTaxable, s.OtherNonTaxable, s.TaxablePay, s.NonTaxablePay, s.GrossPay,
            s.EmploymentInsuranceBase, s.Dependents, s.HealthInsurance, s.CareInsurance, s.Pension, s.PensionFund, s.EmploymentInsurance, s.SocialInsuranceTotal,
            s.TaxableAmount, s.IncomeTax, s.YearEndAdjustment, s.ResidentTax, s.OtherDeduction, s.TotalDeduction, s.NetPay, s.AdvancePayment,
        ];

        /// <summary>
        /// 勤怠データCSVファイル取込形式（マニュアル P614～615）。
        /// スタッフナビゲーターの「給与管理｜勤怠データCSVファイル取込」で、補正後の勤怠を取り込める。スタッフ×受注ごとに1行。
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

        public static object?[] AttendanceCsvRow(MonthView view, StaffMonth staff, OrderSheet sheet)
        {
            var sum = sheet.Summary;
            // 1か月の時間外（スタッフ単位で合算）が45時間・60時間を超えた分は、その受注の普通残として計上する（簡易）
            var total = staff.Summary.TotalOvertimeMinutes;
            var share = total == 0 ? 0m : (decimal)sum.TotalOvertimeMinutes / total;
            var over45 = (int)(Math.Clamp(total - 45 * 60, 0, 15 * 60) * share);
            var over60 = (int)(Math.Max(0, total - 60 * 60) * share);
            var zero = Time(0);
            return
            [
                staff.Staff.EmployeeNo, sheet.Order.OrderNo, view.CloseDate.ToString("yyyy/MM/dd"),
                sum.WorkDays, sum.AbsenceDays, sum.HolidayWorkDays, 0, Fmt.Days(sum.PaidLeaveDays),
                Time(sum.BasicMinutes), Time(sum.OvertimeMinutes), Time(sum.NightMinutes), Time(sum.NightOvertimeMinutes), Time(sum.HolidayMinutes),
                zero, zero, zero, zero, zero, Time(sum.PaidLeaveMinutes),
                zero, Time(over45), zero, zero, zero, zero, zero, zero, zero,
                zero, Time(over60), zero, zero, zero, zero, zero, zero, zero,
                sum.SpecialLeaveDays, sum.Leave2Days, sum.Leave3Days, sum.Leave4Days,
            ];
        }

        /// <summary>“時間:分”形式（例：01:30）</summary>
        public static string Time(int minutes) => $"{minutes / 60:00}:{minutes % 60:00}";

        /// <summary>スタッフナビゲーターに合わせ、Shift_JISで出力する</summary>
        public static byte[] ToShiftJis(string csv) => Encoding.GetEncoding(932).GetBytes(csv);
    }
}
