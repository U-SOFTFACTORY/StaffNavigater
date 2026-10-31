using System.Globalization;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// 賃金台帳の1か月分（スタッフナビゲーターの賃金台帳CSVの項目）。
    /// 給与計算書CSV（スタッフナビゲーターの計算結果）から作るか、未取込の月はタイムシートから試算する。
    /// </summary>
    public class LedgerMonth
    {
        public string EmployeeNo { get; set; } = "";
        public string Name { get; set; } = "";
        public string Gender { get; set; } = "";
        public string Category { get; set; } = "";
        public DateOnly PayDate { get; set; }
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
        /// <summary>true：タイムシートからの試算、false：給与計算書CSVの値</summary>
        public bool IsEstimate { get; set; }
        /// <summary>元データ（取込ファイル名）</summary>
        public string Source { get; set; } = "";
        public int CorrectionCount { get; set; }

        public decimal WorkDays { get; set; }
        public decimal AbsenceDays { get; set; }
        public decimal HolidayWorkDays { get; set; }
        public decimal PaidLeaveDays { get; set; }
        public decimal LateEarlyDays { get; set; }
        public decimal SpecialLeaveDays { get; set; }
        public decimal Leave2Days { get; set; }
        public decimal Leave3Days { get; set; }
        public decimal Leave4Days { get; set; }

        public int BasicMinutes { get; set; }
        public int OvertimeMinutes { get; set; }
        public int NightMinutes { get; set; }
        public int NightOvertimeMinutes { get; set; }
        public int HolidayMinutes { get; set; }
        public int OtherOvertimeMinutes { get; set; }
        public int PaidLeaveMinutes { get; set; }
        public int LateEarlyMinutes { get; set; }

        public long BasicPay { get; set; }
        public long OvertimePay { get; set; }
        public long NightPay { get; set; }
        public long NightOvertimePay { get; set; }
        public long HolidayPay { get; set; }
        public long OtherOvertimePay { get; set; }
        public long PaidLeavePay { get; set; }
        public long CommuteNonTaxable { get; set; }
        public long CommuteTaxable { get; set; }
        public long LateEarlyDeduction { get; set; }
        public long AbsenceDeduction { get; set; }
        public long LeavePay { get; set; }
        public long LeaveDeduction { get; set; }
        public long OtherTaxable { get; set; }
        public long OtherNonTaxable { get; set; }
        public long TaxablePay { get; set; }
        public long NonTaxablePay { get; set; }
        public long GrossPay { get; set; }
        public long EmploymentInsuranceBase { get; set; }
        public int Dependents { get; set; }
        public long HealthInsurance { get; set; }
        public long CareInsurance { get; set; }
        public long Pension { get; set; }
        public long PensionFund { get; set; }
        public long EmploymentInsurance { get; set; }
        public long SocialInsuranceTotal { get; set; }
        public long TaxableAmount { get; set; }
        public long IncomeTax { get; set; }
        public long YearEndAdjustment { get; set; }
        public long ResidentTax { get; set; }
        public long OtherDeduction { get; set; }
        public long TotalDeduction { get; set; }
        public long NetPay { get; set; }
        /// <summary>速払金</summary>
        public long AdvancePayment { get; set; }

        public static LedgerMonth FromSummary(StaffMember staff, MonthSummary sum, DateOnly payDate, DateOnly periodStart, DateOnly periodEnd) => new()
        {
            EmployeeNo = staff.EmployeeNo,
            Name = staff.Name,
            Gender = staff.Gender,
            Category = staff.Category,
            PayDate = payDate,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            CorrectionCount = sum.CorrectionCount,
            WorkDays = sum.WorkDays,
            AbsenceDays = sum.AbsenceDays,
            HolidayWorkDays = sum.HolidayWorkDays,
            PaidLeaveDays = sum.PaidLeaveDays,
            SpecialLeaveDays = sum.SpecialLeaveDays,
            Leave2Days = sum.Leave2Days,
            Leave3Days = sum.Leave3Days,
            Leave4Days = sum.Leave4Days,
            BasicMinutes = sum.BasicMinutes,
            OvertimeMinutes = sum.OvertimeMinutes,
            NightMinutes = sum.NightMinutes,
            NightOvertimeMinutes = sum.NightOvertimeMinutes,
            HolidayMinutes = sum.HolidayMinutes,
            PaidLeaveMinutes = sum.PaidLeaveMinutes,
            Dependents = staff.Dependents,
        };

        /// <summary>給与計算書CSV（26.10支給給与計算書.csv の形式）の1行から作る</summary>
        public static LedgerMonth FromPayroll(PayrollRecord record, StaffMember? staff, string source)
        {
            var v = record.Values;
            long Yen(string name) => long.TryParse(v.GetValueOrDefault(name, "").Replace(",", ""), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var x) ? x : 0;
            decimal Num(string name) => decimal.TryParse(v.GetValueOrDefault(name, ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? x : 0;
            int Time(string name) => Fmt.ParseDuration(v.GetValueOrDefault(name));
            long Sum(params string[] names) => names.Sum(Yen);

            var periodEnd = new DateOnly(record.PayDate.Year, record.PayDate.Month, 1).AddDays(-1);
            var ledger = new LedgerMonth
            {
                EmployeeNo = record.EmployeeNo,
                Name = staff?.Name ?? record.Name,
                Gender = staff?.Gender ?? "",
                Category = v.GetValueOrDefault("ｽﾀｯﾌ区分", staff?.Category ?? ""),
                PayDate = record.PayDate,
                PeriodStart = new DateOnly(periodEnd.Year, periodEnd.Month, 1),
                PeriodEnd = periodEnd,
                Source = source,
                WorkDays = Num("出勤日数"),
                AbsenceDays = Num("欠勤日数"),
                HolidayWorkDays = Num("休出日数"),
                PaidLeaveDays = Num("有給日数"),
                LateEarlyDays = Num("遅早日数"),
                SpecialLeaveDays = Num("特別休暇日数"),
                Leave2Days = Num("休暇２日数"),
                Leave3Days = Num("休暇３日数"),
                Leave4Days = Num("休暇４日数"),
                BasicMinutes = Time("基本時間"),
                OvertimeMinutes = Time("普通残時間"),
                NightMinutes = Time("深夜時間"),
                NightOvertimeMinutes = Time("深夜残時間"),
                HolidayMinutes = Time("休出時間"),
                OtherOvertimeMinutes = Time("その他時間外"),
                PaidLeaveMinutes = Time("有給時間"),
                LateEarlyMinutes = Time("遅早"),
                BasicPay = Yen("基本"),
                OvertimePay = Yen("普通残"),
                NightPay = Yen("深夜"),
                NightOvertimePay = Yen("深夜残"),
                HolidayPay = Yen("休出"),
                OtherOvertimePay = Yen("その他時間外手当"),
                PaidLeavePay = Yen("有給手当"),
                // 給与計算書の「交通費」は非課税として扱う（課税分がある場合は要確認）
                CommuteNonTaxable = Yen("交通費"),
                LateEarlyDeduction = Yen("遅早控除"),
                AbsenceDeduction = Yen("欠勤控除"),
                LeavePay = Yen("休暇手当"),
                LeaveDeduction = Yen("休暇控除"),
                // 課税他１～１０・非課税他１～５は項目名を変更して使われている（例：課税他７＝休業補償、非課税他１＝保安講習受講）
                OtherTaxable = Sum(PayrollFormat.TaxableOthers(v.Keys)),
                OtherNonTaxable = Sum(PayrollFormat.NonTaxableOthers(v.Keys)),
                GrossPay = Yen("総支給額"),
                EmploymentInsuranceBase = Yen("雇用保険対象額"),
                Dependents = staff?.Dependents ?? 0,
                HealthInsurance = Yen("健康保険"),
                CareInsurance = Yen("介護保険"),
                Pension = Yen("厚生年金"),
                PensionFund = Yen("厚生年金基金"),
                EmploymentInsurance = Yen("雇用保険"),
                SocialInsuranceTotal = Yen("社保合計額"),
                TaxableAmount = Yen("課税対象額"),
                IncomeTax = Yen("所得税"),
                YearEndAdjustment = Yen("年調過不足額"),
                ResidentTax = Yen("住民税"),
                OtherDeduction = Sum(PayrollFormat.OtherDeductions(v.Keys)),
                TotalDeduction = Yen("総控除額"),
                NetPay = Yen("差引支給額"),
            };
            ledger.NonTaxablePay = ledger.CommuteNonTaxable + ledger.OtherNonTaxable;
            ledger.TaxablePay = ledger.GrossPay - ledger.NonTaxablePay;
            return ledger;
        }
    }

    /// <summary>給与計算書CSV（スタッフナビゲーターの給与計算書出力）の項目</summary>
    public static class PayrollFormat
    {
        /// <summary>実データ（26.10支給給与計算書.csv）の項目と並び順（76項目）</summary>
        public static readonly string[] Headers =
        [
            "ｽﾀｯﾌ番号", "ｽﾀｯﾌ氏名", "ｽﾀｯﾌ氏名ｶﾅ", "ｽﾀｯﾌ区分", "出勤日数", "欠勤日数", "休出日数", "有給日数", "遅早日数", "特別休暇日数",
            "休暇２日数", "休暇３日数", "休暇４日数", "有給残日数", "基本時間", "普通残時間", "深夜時間", "深夜残時間", "休出時間", "その他時間外",
            "有給時間", "遅早", "有給残時間", "基本", "普通残", "深夜", "深夜残", "休出", "その他時間外手当", "有給手当",
            "交通費", "遅早控除", "欠勤控除", "休暇手当", "休暇控除", "課税他１", "課税他２", "課税他３", "課税他４", "課税他５",
            "課税他６", "休業補償", "課税他８", "課税他９", "課税他１０", "保安講習受講", "非課税他２", "非課税他３", "非課税他４", "非課税他５",
            "総支給額", "雇用保険対象額", "健康保険", "介護保険", "厚生年金", "厚生年金基金", "雇用保険", "社保合計額", "課税対象額", "所得税",
            "年調過不足額", "住民税", "債権差押", "控除2", "控除３", "控除４", "紙明細発行料", "控除６", "控除７", "控除８",
            "控除９", "控除１０", "総控除額", "差引支給額", "支払日", "支払方法",
        ];

        /// <summary>取込に必須の項目</summary>
        public static readonly string[] Required = ["ｽﾀｯﾌ番号", "ｽﾀｯﾌ氏名", "出勤日数", "基本時間", "総支給額", "総控除額", "差引支給額", "支払日"];

        private static int Index(string name) => Array.IndexOf(Headers, name);

        /// <summary>課税他１～課税他１０の位置にある項目（項目名は会社ごとに変更される）</summary>
        public static string[] TaxableOthers(IEnumerable<string> keys) => Range(keys, "課税他１", "課税他１０");

        public static string[] NonTaxableOthers(IEnumerable<string> keys) => Range(keys, "保安講習受講", "非課税他５");

        public static string[] OtherDeductions(IEnumerable<string> keys) => Range(keys, "債権差押", "控除１０");

        private static string[] Range(IEnumerable<string> keys, string first, string last)
        {
            var list = keys.ToList();
            var from = list.IndexOf(first);
            var to = list.IndexOf(last);
            if (from < 0 || to < from)
            {
                // 項目名が異なる場合は実データの並び順の位置で判断する
                from = Index(first);
                to = Index(last);
                return list.Count == Headers.Length ? list.Skip(from).Take(to - from + 1).ToArray() : [];
            }
            return list.Skip(from).Take(to - from + 1).ToArray();
        }
    }
}
