using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// 1日分の労働時間。区分はスタッフナビゲーターの単価区分（基本／普通残／深夜／深夜残／休出）に合わせ、互いに重複しない。
    /// </summary>
    public record DayCalc(int WorkMinutes, int BasicMinutes, int OvertimeMinutes, int NightMinutes, int NightOvertimeMinutes, int HolidayMinutes)
    {
        public static readonly DayCalc Empty = new(0, 0, 0, 0, 0, 0);
    }

    public class MonthSummary
    {
        public int WorkDays { get; set; }
        public int HolidayWorkDays { get; set; }
        public int PaidLeaveDays { get; set; }
        public int AbsenceDays { get; set; }
        public int DayOffDays { get; set; }
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
        public int WarningCount { get; set; }
        public int CorrectionCount { get; set; }

        public int AttendanceDays => WorkDays + HolidayWorkDays;

        /// <summary>時間外労働の合計（普通残＋深夜残）</summary>
        public int TotalOvertimeMinutes => OvertimeMinutes + NightOvertimeMinutes;
    }

    /// <summary>1か月分の賃金（項目名はスタッフナビゲーターの賃金台帳に合わせる）</summary>
    public class WageResult
    {
        /// <summary>基本</summary>
        public long BasicPay { get; set; }
        /// <summary>普通残</summary>
        public long OvertimePay { get; set; }
        /// <summary>深夜</summary>
        public long NightPay { get; set; }
        /// <summary>深夜残</summary>
        public long NightOvertimePay { get; set; }
        /// <summary>休出</summary>
        public long HolidayPay { get; set; }
        /// <summary>有給手当</summary>
        public long PaidLeavePay { get; set; }
        /// <summary>交通費非課税</summary>
        public long CommuteNonTaxable { get; set; }
        public int Dependents { get; set; }
        public long HealthInsurance { get; set; }
        public long CareInsurance { get; set; }
        public long Pension { get; set; }
        public long EmploymentInsurance { get; set; }
        public long IncomeTax { get; set; }
        public long ResidentTax { get; set; }

        /// <summary>課税支給額</summary>
        public long TaxablePay => BasicPay + OvertimePay + NightPay + NightOvertimePay + HolidayPay + PaidLeavePay;
        /// <summary>非課税支給額</summary>
        public long NonTaxablePay => CommuteNonTaxable;
        /// <summary>総支給額</summary>
        public long GrossPay => TaxablePay + NonTaxablePay;
        /// <summary>雇用保険対象額</summary>
        public long EmploymentInsuranceBase => GrossPay;
        /// <summary>社保合計</summary>
        public long SocialInsuranceTotal => HealthInsurance + CareInsurance + Pension + EmploymentInsurance;
        /// <summary>課税対象額</summary>
        public long TaxableAmount => Math.Max(0, TaxablePay - SocialInsuranceTotal);
        /// <summary>総控除額</summary>
        public long TotalDeduction => SocialInsuranceTotal + IncomeTax + ResidentTax;
        /// <summary>差引支給額</summary>
        public long NetPay => GrossPay - TotalDeduction;
    }

    /// <summary>
    /// 労働時間・賃金の計算（デモ用の簡易計算）
    /// </summary>
    public static class LaborCalculator
    {
        private const int NightStart = 22 * 60;
        private const int NightEnd = 29 * 60;
        private const int EarlyNightEnd = 5 * 60;

        public static DayCalc Calculate(AttendanceValues? values, LaborSettings settings)
        {
            if (values == null || !values.Kind.IsWorking() || values.Start == null || values.End == null)
            {
                return DayCalc.Empty;
            }
            var (start, end) = ToMinutes(values.Start.Value, values.End.Value);
            var work = Math.Max(0, end - start - values.BreakMinutes);
            if (values.Kind == DayKind.HolidayWork)
            {
                return new DayCalc(work, 0, 0, 0, 0, work);
            }
            // 休憩は所定時間内に取得し、時間外は勤務の最後に発生したものとして深夜残を求める
            var overtime = Math.Max(0, work - settings.DailyStandardMinutes);
            var nightOvertime = Night(Math.Max(start, end - overtime), end);
            var night = Math.Min(Night(start, end) - nightOvertime, work - overtime);
            return new DayCalc(work, work - overtime - night, overtime - nightOvertime, night, nightOvertime, 0);
        }

        public static List<string> Validate(AttendanceValues? values, DayCalc calc)
        {
            var warnings = new List<string>();
            if (values == null || !values.Kind.IsWorking())
            {
                return warnings;
            }
            if (values.Start == null || values.End == null)
            {
                warnings.Add(values.Start == null && values.End == null ? "出勤・退勤時刻が未入力です（打刻漏れ）" :
                    values.Start == null ? "出勤時刻が未入力です（打刻漏れ）" : "退勤時刻が未入力です（打刻漏れ）");
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

        public static WageResult CalculateWage(StaffMember staff, MonthSummary summary, LaborSettings settings, DateOnly month)
        {
            decimal wage = staff.HourlyWage;
            var result = new WageResult
            {
                BasicPay = Floor(summary.BasicMinutes / 60m * wage),
                OvertimePay = Floor(summary.OvertimeMinutes / 60m * wage * settings.OvertimeRate),
                NightPay = Floor(summary.NightMinutes / 60m * wage * (1 + settings.NightRate)),
                NightOvertimePay = Floor(summary.NightOvertimeMinutes / 60m * wage * (settings.OvertimeRate + settings.NightRate)),
                HolidayPay = Floor(summary.HolidayMinutes / 60m * wage * settings.HolidayRate),
                PaidLeavePay = Floor(summary.PaidLeaveMinutes / 60m * wage),
                CommuteNonTaxable = (long)summary.AttendanceDays * staff.CommuteAllowancePerDay,
                Dependents = staff.Dependents,
            };
            var gross = result.GrossPay;
            if (gross == 0)
            {
                return result;
            }
            result.HealthInsurance = Round(gross * settings.HealthInsuranceRate);
            result.CareInsurance = staff.AgeAt(month.AddMonths(1).AddDays(-1)) is >= 40 and < 65 ? Round(gross * settings.CareInsuranceRate) : 0;
            result.Pension = Round(gross * settings.PensionRate);
            result.EmploymentInsurance = Round(gross * settings.EmploymentInsuranceRate);
            result.IncomeTax = Floor(result.TaxableAmount * settings.IncomeTaxRate * (1 - Math.Min(staff.Dependents, 7) * 0.1m));
            result.ResidentTax = staff.ResidentTax;
            return result;
        }

        private static (int Start, int End) ToMinutes(TimeOnly start, TimeOnly end)
        {
            var s = start.Hour * 60 + start.Minute;
            var e = end.Hour * 60 + end.Minute;
            if (e <= s)
            {
                e += 24 * 60;
            }
            return (s, e);
        }

        private static int Night(int start, int end) =>
            Overlap(start, end, 0, EarlyNightEnd) + Overlap(start, end, NightStart, NightEnd);

        private static int Overlap(int start, int end, int rangeStart, int rangeEnd) =>
            Math.Max(0, Math.Min(end, rangeEnd) - Math.Max(start, rangeStart));

        private static long Floor(decimal value) => (long)Math.Floor(value);

        private static long Round(decimal value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
