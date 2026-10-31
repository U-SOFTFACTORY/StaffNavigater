using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// 1日分の労働時間。区分はスタッフナビゲーターのタイムシートの時間区分（基本／普通残／深夜／深夜残／休出）に合わせ、互いに重複しない。
    /// </summary>
    public record DayCalc(int WorkMinutes, int BasicMinutes, int OvertimeMinutes, int NightMinutes, int NightOvertimeMinutes, int HolidayMinutes, int PaidLeaveMinutes)
    {
        public static readonly DayCalc Empty = new(0, 0, 0, 0, 0, 0, 0);

        /// <summary>タイムシートの「合計」（労働時間＋有給時間）</summary>
        public int TotalMinutes => WorkMinutes + PaidLeaveMinutes;
    }

    public class MonthSummary
    {
        /// <summary>出勤日数（複数の受注で勤務した日も1日と数える）</summary>
        public int WorkDays { get; set; }
        public int HolidayWorkDays { get; set; }
        public int AbsenceDays { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public int SpecialLeaveDays { get; set; }
        public int Leave2Days { get; set; }
        public int Leave3Days { get; set; }
        public int Leave4Days { get; set; }
        public int WorkMinutes { get; set; }
        /// <summary>基本時間</summary>
        public int BasicMinutes { get; set; }
        /// <summary>普通残時間</summary>
        public int OvertimeMinutes { get; set; }
        /// <summary>深夜時間（所定内の深夜）</summary>
        public int NightMinutes { get; set; }
        /// <summary>深夜残時間（時間外の深夜）</summary>
        public int NightOvertimeMinutes { get; set; }
        /// <summary>休出時間</summary>
        public int HolidayMinutes { get; set; }
        /// <summary>有給時間</summary>
        public int PaidLeaveMinutes { get; set; }
        public int BreakMinutes { get; set; }
        public int WarningCount { get; set; }
        public int CorrectionCount { get; set; }

        public int AttendanceDays => WorkDays + HolidayWorkDays;

        public int TotalMinutes => WorkMinutes + PaidLeaveMinutes;

        /// <summary>時間外労働の合計（普通残＋深夜残）</summary>
        public int TotalOvertimeMinutes => OvertimeMinutes + NightOvertimeMinutes;
    }

    /// <summary>
    /// 労働時間の計算と、給与計算書が未取込の月の賃金の試算
    /// </summary>
    public static class LaborCalculator
    {
        private const int Day = 24 * 60;
        private const int NightStart = 22 * 60;
        private const int NightEnd = 5 * 60;

        /// <summary>
        /// 実際の勤務表（実績）と同じ考え方で時間区分を求める。
        /// 休憩は深夜帯から差し引き（深夜帯がなければ日中から）、始業から数えて受注の所定労働時間を超えた分を普通残・深夜残とする。
        /// 例：所定7時間・22:00-32:00・休憩1:00 → 基本1:00／普通残2:00／深夜6:00（合計9:00）
        /// </summary>
        public static DayCalc Calculate(AttendanceValues? values, WorkOrder order, decimal paidLeaveHoursPerDay = 8)
        {
            var paid = PaidLeaveMinutes(values?.Kind, paidLeaveHoursPerDay);
            if (values == null || !values.Kind.HasTimes() || values.Start == null || values.End == null)
            {
                return DayCalc.Empty with { PaidLeaveMinutes = paid };
            }
            var start = values.Start.Value;
            var end = values.End.Value <= start ? values.End.Value + Day : values.End.Value;
            var minutes = new List<bool>(end - start);
            for (var t = start; t < end; t++)
            {
                minutes.Add(IsNight(t));
            }
            RemoveBreak(minutes, values.BreakMinutes);
            var work = minutes.Count;
            if (values.Kind == DayKind.HolidayWork)
            {
                return new DayCalc(work, 0, 0, 0, 0, work, paid);
            }
            var standard = Math.Max(0, order.StandardMinutes - paid);
            int basic = 0, overtime = 0, night = 0, nightOvertime = 0;
            for (var i = 0; i < minutes.Count; i++)
            {
                var over = i >= standard;
                if (minutes[i])
                {
                    if (over) nightOvertime++; else night++;
                }
                else
                {
                    if (over) overtime++; else basic++;
                }
            }
            return new DayCalc(work, basic, overtime, night, nightOvertime, 0, paid);
        }

        public static int PaidLeaveMinutes(DayKind? kind, decimal hoursPerDay) => kind switch
        {
            DayKind.PaidLeave => (int)(hoursPerDay * 60),
            DayKind.HalfPaidLeave => (int)(hoursPerDay * 30),
            _ => 0,
        };

        public static List<string> Validate(AttendanceValues? values, DayCalc calc)
        {
            var warnings = new List<string>();
            if (values == null || !values.Kind.HasTimes())
            {
                return warnings;
            }
            if (values.Start == null || values.End == null)
            {
                if (values.Kind.RequiresTimes() || values.Start != null || values.End != null)
                {
                    warnings.Add(values.Start == null && values.End == null ? "始業・終業が未入力です（打刻漏れ）" :
                        values.Start == null ? "始業が未入力です（打刻漏れ）" : "終業が未入力です（打刻漏れ）");
                }
                return warnings;
            }
            if (calc.WorkMinutes > 480 && values.BreakMinutes < 60)
            {
                warnings.Add("休憩時間不足（8時間超の勤務は60分以上）");
            }
            else if (calc.WorkMinutes > 360 && values.BreakMinutes < 45)
            {
                warnings.Add("休憩時間不足（6時間超の勤務は45分以上）");
            }
            if (calc.WorkMinutes > 720)
            {
                warnings.Add("長時間勤務（1日12時間超）");
            }
            return warnings;
        }

        /// <summary>
        /// 給与計算書が未取込の月の試算（デモ用の簡易計算）。単価・交通費は受注ごと、社会保険料は等級の標準報酬月額×料率で求める。
        /// </summary>
        public static LedgerMonth Estimate(StaffMonth staffMonth, LaborSettings settings, DateOnly payDate, DateOnly periodStart, DateOnly periodEnd)
        {
            var staff = staffMonth.Staff;
            var sum = staffMonth.Summary;
            var ledger = LedgerMonth.FromSummary(staff, sum, payDate, periodStart, periodEnd);
            ledger.IsEstimate = true;
            foreach (var sheet in staffMonth.Sheets)
            {
                decimal wage = sheet.Order.HourlyWage;
                var s = sheet.Summary;
                ledger.BasicPay += Floor(s.BasicMinutes / 60m * wage);
                ledger.OvertimePay += Floor(s.OvertimeMinutes / 60m * wage * settings.OvertimeRate);
                ledger.NightPay += Floor(s.NightMinutes / 60m * wage * (1 + settings.NightRate));
                ledger.NightOvertimePay += Floor(s.NightOvertimeMinutes / 60m * wage * (settings.OvertimeRate + settings.NightRate));
                ledger.HolidayPay += Floor(s.HolidayMinutes / 60m * wage * settings.HolidayRate);
                ledger.PaidLeavePay += Floor(s.PaidLeaveMinutes / 60m * wage);
                ledger.CommuteNonTaxable += (long)s.AttendanceDays * sheet.Order.CommutePerDay;
            }
            ledger.TaxablePay = ledger.BasicPay + ledger.OvertimePay + ledger.NightPay + ledger.NightOvertimePay + ledger.HolidayPay + ledger.PaidLeavePay;
            ledger.NonTaxablePay = ledger.CommuteNonTaxable;
            ledger.GrossPay = ledger.TaxablePay + ledger.NonTaxablePay;
            if (ledger.GrossPay == 0)
            {
                return ledger;
            }
            ledger.EmploymentInsuranceBase = ledger.GrossPay;
            if (staff.HealthInsured)
            {
                var standard = StandardRemuneration.Health(staff.HealthGrade);
                ledger.HealthInsurance = Round(standard * settings.HealthInsuranceRate);
                ledger.CareInsurance = staff.CareInsured ? Round(standard * settings.CareInsuranceRate) : 0;
            }
            if (staff.PensionInsured)
            {
                ledger.Pension = Round(StandardRemuneration.Pension(staff.PensionGrade) * settings.PensionRate);
            }
            ledger.EmploymentInsurance = staff.EmploymentInsured ? Floor(ledger.GrossPay * settings.EmploymentInsuranceRate) : 0;
            ledger.SocialInsuranceTotal = ledger.HealthInsurance + ledger.CareInsurance + ledger.Pension + ledger.EmploymentInsurance;
            ledger.TaxableAmount = Math.Max(0, ledger.TaxablePay - ledger.SocialInsuranceTotal);
            ledger.IncomeTax = staff.TaxCategory == "乙欄"
                ? Floor(ledger.TaxableAmount * settings.IncomeTaxRateOtsu)
                : staff.TaxCategory == "無税" ? 0 : Floor(ledger.TaxableAmount * settings.IncomeTaxRate * (1 - Math.Min(staff.Dependents, 7) * 0.1m));
            ledger.ResidentTax = staff.ResidentTax;
            ledger.TotalDeduction = ledger.SocialInsuranceTotal + ledger.IncomeTax + ledger.ResidentTax;
            ledger.NetPay = ledger.GrossPay - ledger.TotalDeduction;
            return ledger;
        }

        private static bool IsNight(int minute)
        {
            var t = minute % Day;
            return t >= NightStart || t < NightEnd;
        }

        /// <summary>休憩分を深夜帯の最初から差し引く（深夜帯が休憩より短い場合は勤務の中間から差し引く）</summary>
        private static void RemoveBreak(List<bool> minutes, int breakMinutes)
        {
            var remaining = Math.Min(Math.Max(0, breakMinutes), minutes.Count);
            if (remaining == 0)
            {
                return;
            }
            if (minutes.Count(m => m) >= remaining)
            {
                var first = minutes.IndexOf(true);
                for (var i = first; i < minutes.Count && remaining > 0;)
                {
                    if (minutes[i])
                    {
                        minutes.RemoveAt(i);
                        remaining--;
                    }
                    else
                    {
                        i++;
                    }
                }
                return;
            }
            var middle = Math.Max(0, (minutes.Count - remaining) / 2);
            minutes.RemoveRange(middle, remaining);
        }

        public static long Floor(decimal value) => (long)Math.Floor(value);

        public static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }

    /// <summary>標準報酬月額（健康保険は1～50等級、厚生年金は1～32等級）</summary>
    public static class StandardRemuneration
    {
        private static readonly int[] HealthTable =
        [
            58_000, 68_000, 78_000, 88_000, 98_000, 104_000, 110_000, 118_000, 126_000, 134_000,
            142_000, 150_000, 160_000, 170_000, 180_000, 190_000, 200_000, 220_000, 240_000, 260_000,
            280_000, 300_000, 320_000, 340_000, 360_000, 380_000, 410_000, 440_000, 470_000, 500_000,
            530_000, 560_000, 590_000, 620_000, 650_000, 680_000, 710_000, 750_000, 790_000, 830_000,
            880_000, 930_000, 980_000, 1_030_000, 1_090_000, 1_150_000, 1_210_000, 1_270_000, 1_330_000, 1_390_000,
        ];

        public static int Health(int grade) => HealthTable[Math.Clamp(grade, 1, 50) - 1];

        /// <summary>厚生年金の等級 n は健康保険の等級 n+3 と同じ金額（上限は32等級 650,000円）</summary>
        public static int Pension(int grade) => HealthTable[Math.Clamp(grade, 1, 32) + 2];

        /// <summary>報酬月額に対応する健康保険の等級（試算データ作成用）</summary>
        public static int HealthGradeFor(long monthly)
        {
            for (var i = 0; i < HealthTable.Length - 1; i++)
            {
                if (monthly < (HealthTable[i] + HealthTable[i + 1]) / 2)
                {
                    return i + 1;
                }
            }
            return 50;
        }

        public static int PensionGradeFor(int healthGrade) => Math.Clamp(healthGrade - 3, 1, 32);
    }
}
