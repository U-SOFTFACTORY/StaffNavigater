using System.Text;
using StaffNavi.Models;

namespace StaffNavi.Services
{
    /// <summary>
    /// デモ用ダミーデータの生成。先方から提供された実データの「形式・構造」（受注ごとのタイムシート、SSの勤務帯、掛け持ち、
    /// 夜勤の日またぎ、スタッフ区分、社会保険の等級など）をまねた架空のデータで、実在の人物・企業とは関係ありません。
    /// </summary>
    public class DemoSeeder(LaborRepository repo, CsvImportService importer, LaborService labor, AdvanceService advance)
    {
        public const string AdminName = "山田 太郎";
        public const string StaffUserName = "鈴木 花子";

        /// <summary>最新の就業月（CSV取込のデモで、追加の受注のサンプルを使う月）</summary>
        public const int LatestYear = 2026;
        public const int LatestMonth = 10;

        /// <summary>デモ開始時点では取り込まず、サンプルCSVとして配布する受注（10月から始まった新しいSS）</summary>
        private static readonly string[] SampleOrderNos = ["21115501", "21115502"];

        private const string ClientA = "株式会社サンプル石油　西東京支店";
        private const string ClientB = "ミナト燃料株式会社";
        private const string ClientC = "株式会社ひかりエネルギー　神奈川支店";
        private const string Job = "SSフィールド内での監視・接客業務";
        private const string JobNight = "SSフィールド内での監視・接客・清掃・事務作業";

        public static List<WorkOrder> CreateOrders()
        {
            WorkOrder O(string no, string name, string client, string workplace, string address, string area, string job, string shift,
                int start, int end, int standard, int wage, int commute) => new()
            {
                OrderNo = no, Name = name, Client = client, Workplace = workplace, Address = address, Area = area, JobDescription = job,
                ShiftName = shift, ShiftStart = start, ShiftEnd = end, StandardMinutes = standard, HourlyWage = wage, CommutePerDay = commute,
            };
            const int h = 60;
            return
            [
                O("21053101", "セルフ多摩中央SS A勤", ClientA, "セルフ多摩中央SS", "東京都多摩市中央台1-2-3", "東京", Job, "A勤", 6 * h + 30, 16 * h, 480, 1225, 520),
                O("21053102", "セルフ多摩中央SS B勤", ClientA, "セルフ多摩中央SS", "東京都多摩市中央台1-2-3", "東京", Job, "B勤", 12 * h, 21 * h + 30, 480, 1225, 520),
                O("21053103", "セルフ多摩中央SS 夜間", ClientA, "セルフ多摩中央SS", "東京都多摩市中央台1-2-3", "東京", JobNight, "夜間", 22 * h, 32 * h, 420, 1225, 520),
                O("21071201", "府中東SS 夜間", ClientA, "府中東SS", "東京都府中市東町4-5-6", "東京", JobNight, "夜間", 20 * h, 32 * h, 480, 1250, 438),
                O("21071202", "府中東SS 日勤", ClientA, "府中東SS", "東京都府中市東町4-5-6", "東京", Job, "日勤", 8 * h, 17 * h, 480, 1250, 438),
                O("21088401", "町田南セルフSS A勤", ClientB, "町田南セルフSS", "東京都町田市南が丘7-8", "東京", Job, "A勤", 7 * h, 16 * h, 480, 1200, 600),
                O("21088402", "町田南セルフSS B勤", ClientB, "町田南セルフSS", "東京都町田市南が丘7-8", "東京", Job, "B勤", 13 * h, 22 * h, 480, 1200, 600),
                O("21090501", "川崎北SS 日勤", ClientC, "川崎北SS", "神奈川県川崎市北区北町9-10", "神奈川", Job, "日勤", 9 * h, 18 * h, 480, 1180, 480),
                O("21090502", "川崎北SS 夜間", ClientC, "川崎北SS", "神奈川県川崎市北区北町9-10", "神奈川", JobNight, "夜間", 22 * h, 31 * h, 480, 1180, 480),
                O("21102301", "八王子西SS 夜間", ClientA, "八王子西SS", "東京都八王子市西原11-12", "東京", JobNight, "夜間", 22 * h, 32 * h, 420, 1225, 584),
                O("21115501", "横浜港北セルフSS A勤", ClientC, "横浜港北セルフSS", "神奈川県横浜市港北区新町13-14", "神奈川", Job, "A勤", 6 * h, 15 * h, 480, 1210, 450),
                O("21115502", "横浜港北セルフSS B勤", ClientC, "横浜港北セルフSS", "神奈川県横浜市港北区新町13-14", "神奈川", Job, "B勤", 14 * h, 23 * h, 480, 1210, 450),
            ];
        }

        /// <summary>受注ごとの担当スタッフ（スタッフの並び順の番号）。複数の受注にいるスタッフは掛け持ち</summary>
        private static readonly Dictionary<string, int[]> Pools = new()
        {
            ["21053101"] = [0, 1, 2, 32],
            ["21053102"] = [3, 4, 5, 28],
            ["21053103"] = [6, 7, 30],
            ["21071201"] = [8, 9, 11],
            ["21071202"] = [10, 12, 2, 32],
            ["21088401"] = [13, 14, 15, 29],
            ["21088402"] = [16, 17, 18, 15, 33],
            ["21090501"] = [19, 20, 21, 31],
            ["21090502"] = [22, 23, 21],
            ["21102301"] = [24, 25, 7],
            ["21115501"] = [26, 34, 19],
            ["21115502"] = [27, 35, 4],
        };

        private const string CatNoSocial = "派・ｱﾙ《有雇･社保無》";
        private const string CatSocial = "派・ｱﾙ《有雇･社保有》";
        private const string CatContract = "契【運用】(社保有)";
        private const string CatLeader = "【リーダー】";

        public static List<StaffMember> CreateStaff()
        {
            (string Name, string Kana, string Gender, string Birth, string Hire, string Category, string Tax, int Dependents, int ResidentTax)[] rows =
            [
                ("佐藤 健太", "サトウ ケンタ", "男", "1985/04/12", "2019/04/01", CatSocial, "甲欄", 2, 14800),
                ("遠藤 一郎", "エンドウ イチロウ", "男", "1962/08/03", "2015/07/01", CatNoSocial, "甲欄", 0, 0),
                ("高橋 誠司", "タカハシ セイジ", "男", "1971/01/25", "2017/03/28", CatNoSocial, "甲欄", 1, 0),
                ("田中 浩", "タナカ ヒロシ", "男", "1958/11/09", "2012/04/01", CatNoSocial, "甲欄", 0, 0),
                ("渡辺 隆之", "ワタナベ タカユキ", "男", "1990/06/17", "2021/06/15", CatSocial, "甲欄", 0, 9600),
                ("伊藤 正樹", "イトウ マサキ", "男", "2001/02/28", "2023/04/01", CatNoSocial, "乙欄", 0, 0),
                ("山本 和也", "ヤマモト カズヤ", "男", "1979/09/30", "2016/10/01", CatNoSocial, "甲欄", 1, 0),
                ("中村 修", "ナカムラ オサム", "男", "1966/12/14", "2014/12/27", CatSocial, "甲欄", 0, 21400),
                ("小林 大輔", "コバヤシ ダイスケ", "男", "1988/03/05", "2020/09/01", CatNoSocial, "甲欄", 0, 0),
                ("加藤 進", "カトウ ススム", "男", "1955/07/21", "2011/01/01", CatNoSocial, "乙欄", 0, 0),
                ("吉田 博之", "ヨシダ ヒロユキ", "男", "1975/05/08", "2018/02/01", CatLeader, "甲欄", 3, 7500),
                ("太田 剛", "オオタ ツヨシ", "男", "1983/10/19", "2019/11/01", CatSocial, "甲欄", 1, 6300),
                ("佐々木 学", "ササキ マナブ", "男", "1997/08/11", "2022/05/01", CatNoSocial, "甲欄", 0, 0),
                ("山口 達也", "ヤマグチ タツヤ", "男", "1969/02/02", "2013/12/18", CatNoSocial, "甲欄", 0, 0),
                ("松本 明", "マツモト アキラ", "男", "1960/04/27", "2015/01/03", CatNoSocial, "乙欄", 0, 0),
                ("井上 雄一", "イノウエ ユウイチ", "男", "1981/12/01", "2017/08/01", CatSocial, "甲欄", 2, 12500),
                ("木村 拓海", "キムラ タクミ", "男", "2003/07/07", "2024/04/01", CatNoSocial, "甲欄", 0, 0),
                ("林 勝", "ハヤシ マサル", "男", "1964/03/16", "2012/04/01", CatNoSocial, "甲欄", 0, 0),
                ("斎藤 光男", "サイトウ ミツオ", "男", "1957/10/10", "2011/12/21", CatNoSocial, "甲欄", 1, 0),
                ("清水 亮", "シミズ リョウ", "男", "1992/01/31", "2021/02/01", CatNoSocial, "甲欄", 0, 0),
                ("山崎 翔太", "ヤマザキ ショウタ", "男", "1999/05/22", "2023/07/01", CatNoSocial, "甲欄", 0, 0),
                ("森 稔", "モリ ミノル", "男", "1973/09/04", "2016/03/01", CatContract, "甲欄", 2, 15200),
                ("池田 茂樹", "イケダ シゲキ", "男", "1961/06/13", "2014/06/01", CatNoSocial, "甲欄", 0, 0),
                ("橋本 直人", "ハシモト ナオト", "男", "1986/11/26", "2020/01/06", CatNoSocial, "乙欄", 0, 0),
                ("阿部 智也", "アベ トモヤ", "男", "1977/04/04", "2018/05/01", CatNoSocial, "甲欄", 1, 0),
                ("石川 裕二", "イシカワ ユウジ", "男", "1968/08/29", "2015/07/29", CatNoSocial, "甲欄", 0, 0),
                ("前田 恵子", "マエダ ケイコ", "女", "1980/02/14", "2026/10/01", CatNoSocial, "甲欄", 0, 0),
                ("藤田 美穂", "フジタ ミホ", "女", "1994/09/18", "2026/10/01", CatNoSocial, "甲欄", 0, 0),
                ("小川 誠", "オガワ マコト", "男", "2004/03/03", "2025/03/01", CatNoSocial, "甲欄", 0, 0),
                ("岡田 健二", "オカダ ケンジ", "男", "1972/07/15", "2016/09/01", CatNoSocial, "甲欄", 0, 0),
                ("後藤 悠斗", "ゴトウ ユウト", "男", "2002/12/24", "2025/02/28", CatNoSocial, "甲欄", 0, 0),
                ("長谷川 豊", "ハセガワ ユタカ", "男", "1959/05/05", "2013/04/01", CatNoSocial, "乙欄", 0, 0),
                ("村上 純一", "ムラカミ ジュンイチ", "男", "1984/01/09", "2019/03/01", CatNoSocial, "甲欄", 0, 0),
                ("近藤 秀樹", "コンドウ ヒデキ", "男", "1965/11/30", "2017/03/28", CatNoSocial, "甲欄", 0, 0),
                ("石井 涼", "イシイ リョウ", "男", "1998/06/06", "2026/10/01", CatNoSocial, "甲欄", 0, 0),
                ("坂口 優", "サカグチ ユウ", "男", "1991/10/27", "2026/10/01", CatNoSocial, "乙欄", 0, 0),
            ];
            var staff = new List<StaffMember>();
            for (var i = 0; i < rows.Length; i++)
            {
                var r = rows[i];
                var social = r.Category != CatNoSocial;
                var birth = DateOnly.ParseExact(r.Birth, "yyyy/MM/dd");
                var member = new StaffMember
                {
                    EmployeeNo = StaffNo(i),
                    Name = r.Name,
                    Kana = r.Kana,
                    Gender = r.Gender,
                    BirthDate = birth,
                    HireDate = DateOnly.ParseExact(r.Hire, "yyyy/MM/dd"),
                    Category = r.Category,
                    EmploymentForm = social ? "一般" : i % 5 == 0 ? "短時間労働者" : "一般",
                    HealthInsured = social,
                    PensionInsured = social && birth > new DateOnly(1956, 11, 1),
                    EmploymentInsured = true,
                    TaxCategory = r.Tax,
                    Dependents = r.Dependents,
                    ResidentTax = r.ResidentTax,
                };
                // 社会保険の等級は、勤務の多い社保加入者を想定して設定する（未加入者は実データと同じく1等級）
                var grade = social ? StandardRemuneration.HealthGradeFor(220_000 + i % 5 * 20_000) : 1;
                member.HealthGrade = grade;
                member.PensionGrade = social ? StandardRemuneration.PensionGradeFor(grade) : 1;
                member.CareInsured = social && member.AgeAt(new DateOnly(2026, 10, 1)) is >= 40 and < 65;
                staff.Add(member);
            }
            // 10月15日で退職したスタッフ（退職日以降の勤務データが残っている例）
            staff[33].Retired = true;
            staff[33].RetireDate = new DateOnly(2026, 10, 15);
            return staff;
        }

        /// <summary>スタッフ番号（実データと同じく7桁の数字）</summary>
        private static string StaffNo(int index) => (2003105 + index * 37).ToString();

        /// <summary>
        /// 1か月分のタイムシート（受注ごとに1日1名が勤務。打刻漏れ・休憩不足・掛け持ちの重複などの要確認データを意図的に含む）
        /// </summary>
        public static List<AttendanceRecord> CreateTimesheet(List<StaffMember> staff, List<WorkOrder> orders, int year, int month, bool latest)
        {
            var rng = new Random(year * 100 + month);
            var days = DateTime.DaysInMonth(year, month);
            var records = new List<AttendanceRecord>();
            var busy = new HashSet<(int Staff, int Day)>();
            var counts = new Dictionary<int, int>();
            var monthStart = new DateOnly(year, month, 1);

            foreach (var order in orders)
            {
                // 横浜港北SSは10月から始まった受注
                if (SampleOrderNos.Contains(order.OrderNo) && monthStart < new DateOnly(LatestYear, LatestMonth, 1))
                {
                    continue;
                }
                var pool = Pools[order.OrderNo].Where(i => staff[i].HireDate <= monthStart.AddMonths(1).AddDays(-1)).ToArray();
                for (var d = 1; d <= days; d++)
                {
                    // 前日・当日に入っていない人のうち、勤務日数の少ない人を優先する（同じ人が続かないよう少しばらつかせる）
                    var date = new DateOnly(year, month, d);
                    var candidates = pool.Where(i => !busy.Contains((i, d)) && !(staff[i].RetireDate < date))
                        // 社保加入・契約のスタッフは勤務日数が多い（実データでも月19日前後）
                        .OrderBy(i => counts.GetValueOrDefault(i) / (staff[i].HealthInsured ? 3.0 : 1.0) + (busy.Contains((i, d - 1)) ? 1.5 : 0) + rng.NextDouble() * 1.5)
                        .ToList();
                    if (candidates.Count == 0)
                    {
                        continue;
                    }
                    var who = candidates[0];
                    busy.Add((who, d));
                    counts[who] = counts.GetValueOrDefault(who) + 1;
                    var record = new AttendanceRecord
                    {
                        EmployeeNo = staff[who].EmployeeNo,
                        OrderNo = order.OrderNo,
                        Date = date,
                        DataNo = 1,
                        Kind = DayKind.Work,
                        Start = order.ShiftStart,
                        End = order.ShiftEnd,
                        BreakMinutes = 60,
                    };
                    var roll = rng.NextDouble();
                    if (roll < 0.12)
                    {
                        record.End += 30 * rng.Next(1, 4); // 交代遅れなどの残業
                    }
                    else if (roll < 0.17)
                    {
                        record.Start -= 15; // 早出
                    }
                    records.Add(record);
                }
            }

            // 有給休暇（勤務日数の多いスタッフの勤務を有給に置き換える）
            foreach (var r in records.Where((_, i) => i % 47 == 13).ToList())
            {
                r.Kind = DayKind.PaidLeave;
                r.Start = r.End = null;
                r.BreakMinutes = 0;
            }

            AttendanceRecord? Find(string orderNo, int day) => records.FirstOrDefault(r => r.OrderNo == orderNo && r.Date.Day == day && r.Kind == DayKind.Work);
            void SetStaff(AttendanceRecord? r, int index)
            {
                if (r != null)
                {
                    r.EmployeeNo = staff[index].EmployeeNo;
                }
            }

            if (latest)
            {
                // 打刻漏れ（終業が空欄。実データのタイムシートCSVにもある）
                if (Find("21053101", 21) is { } a) a.End = null;
                if (Find("21053102", 29) is { } b) b.End = null;
                if (Find("21090502", 12) is { } c) c.End = null;
                // 休憩不足
                if (Find("21071202", 20) is { } e) { e.BreakMinutes = 30; e.End += 60; }
                // 掛け持ちで勤務時間が重複（2つのSSの夜間に同じ日に入っている）
                SetStaff(Find("21053103", 14), 7);
                SetStaff(Find("21102301", 14), 7);
                // 掛け持ちで同日の合計が8時間超（日勤のあと別の受注の夜間）
                SetStaff(Find("21090501", 9), 21);
                SetStaff(Find("21090502", 9), 21);
                // 退職日（10/15）以降の勤務
                SetStaff(Find("21088402", 20), 33);
            }
            else
            {
                // 締め済みの月にも打刻漏れ・休憩不足があり、補正済みにする
                if (Find("21088401", 6 + month % 9) is { } a) a.End = null;
                if (Find("21053102", 11 + month % 7) is { } b) { b.BreakMinutes = 30; b.End += 60; }
                if (month % 2 == 0 && Find("21071201", 17) is { } c) c.Start = null;
            }
            return records.OrderBy(r => r.OrderNo).ThenBy(r => r.Date).ToList();
        }

        /// <summary>タイムシートCSV（実データ「タイムシートA勤.csv」と同じ形式。行末にカンマが付く）</summary>
        public static string TimesheetCsv(IEnumerable<AttendanceRecord> records)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", CsvImportService.TimesheetHeaders.Select(Quote))).Append("\r\n");
            foreach (var r in records)
            {
                // 日をまたぐ終業は、スタッフナビゲーターの出力に合わせて翌日の時刻（例：8:00）で出力する
                var end = r.End is { } e && e >= 24 * 60 ? e - 24 * 60 : r.End;
                var values = new object?[]
                {
                    r.EmployeeNo, r.OrderNo, r.Date.ToString("yyyy/MM/dd"), r.DataNo, r.Kind.StatusCode(),
                    Fmt.Clock(r.Start), Fmt.Clock(end), r.Kind.HasTimes() ? r.BreakMinutes : 0,
                };
                sb.Append(string.Join(",", values.Select(v => Quote(Convert.ToString(v) ?? "")))).Append(",\r\n");
            }
            return sb.ToString();
        }

        private static string Quote(string text) => $"\"{text.Replace("\"", "\"\"")}\"";

        /// <summary>スタッフ全データCSV（実データと同じ202項目。デモでは労務資料に使う項目だけ値を入れる）</summary>
        public static string StaffCsv(IEnumerable<StaffMember> staff)
        {
            var headers = StaffFullHeaders;
            var rows = new List<IEnumerable<object?>> { headers };
            foreach (var s in staff)
            {
                var social = s.HealthInsured;
                var values = new Dictionary<string, string?>
                {
                    ["ｽﾀｯﾌ番号"] = s.EmployeeNo,
                    ["登録日"] = s.HireDate?.AddDays(-7).ToString("yyyy/MM/dd"),
                    ["検索対象"] = "対象とする",
                    ["ｽﾀｯﾌ区分"] = s.Category,
                    ["氏名(ｶﾅ)"] = s.Kana,
                    ["氏名(漢字)"] = s.Name,
                    ["性別"] = s.Gender,
                    ["生年月日"] = s.BirthDate?.ToString("yyyy/MM/dd"),
                    ["年齢"] = s.AgeAt(new DateOnly(2026, 11, 1))?.ToString(),
                    ["入社日"] = s.HireDate?.ToString("yyyy/MM/dd"),
                    ["退職"] = s.Retired ? "退職" : "在職",
                    ["退職日"] = s.RetireDate?.ToString("yyyy/MM/dd"),
                    ["稼働実績"] = "有り",
                    ["１日あたり有給時間"] = "8",
                    ["支払形態"] = "月１回",
                    ["給与締日1"] = "月末",
                    ["給与支払日1"] = "25",
                    ["雇用形態"] = s.EmploymentForm,
                    ["厚生年金加入状況"] = s.PensionInsured ? "加入済" : "資格なし",
                    ["厚生年金等級"] = s.PensionGrade.ToString(),
                    ["健康保険加入状況"] = social ? "加入済" : "資格なし",
                    ["健康保険等級"] = s.HealthGrade.ToString(),
                    ["介護保険"] = s.CareInsured ? "対象" : "非対象",
                    ["保険料率"] = "東京",
                    ["雇用保険加入状況"] = s.EmploymentInsured ? "加入済" : "資格なし",
                    ["税区分"] = s.TaxCategory,
                    ["扶養家族人数"] = s.Dependents.ToString(),
                    ["住民税区分"] = s.ResidentTax > 0 ? "特別徴収" : "普通徴収",
                    ["住民税(6月分)"] = s.ResidentTax > 0 ? (s.ResidentTax + 500).ToString() : "0",
                    ["住民税(7月以降)"] = s.ResidentTax.ToString(),
                };
                rows.Add(headers.Select(h => (object?)values.GetValueOrDefault(h)));
            }
            return CsvText.Write(rows);
        }

        /// <summary>給与計算書CSV（実データ「26.10支給給与計算書.csv」と同じ76項目）。デモでは本システムの試算値で作成する</summary>
        public static string PayrollCsv(IEnumerable<(StaffMember Staff, LedgerMonth Ledger)> rows)
        {
            var list = new List<IEnumerable<object?>> { PayrollFormat.Headers };
            foreach (var (staff, s) in rows)
            {
                string T(int minutes) => $"{minutes / 60}:{minutes % 60:00}";
                list.Add(new object?[]
                {
                    staff.EmployeeNo, staff.Name, staff.Kana, staff.Category, Fmt.Days(s.WorkDays), Fmt.Days(s.AbsenceDays), Fmt.Days(s.HolidayWorkDays), Fmt.Days(s.PaidLeaveDays), 0, Fmt.Days(s.SpecialLeaveDays),
                    Fmt.Days(s.Leave2Days), Fmt.Days(s.Leave3Days), Fmt.Days(s.Leave4Days), 10 + int.Parse(staff.EmployeeNo) % 20, T(s.BasicMinutes), T(s.OvertimeMinutes), T(s.NightMinutes), T(s.NightOvertimeMinutes), T(s.HolidayMinutes), T(0),
                    T(s.PaidLeaveMinutes), T(0), T(0), s.BasicPay, s.OvertimePay, s.NightPay, s.NightOvertimePay, s.HolidayPay, 0, s.PaidLeavePay,
                    s.CommuteNonTaxable, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                    s.GrossPay, s.EmploymentInsuranceBase, s.HealthInsurance, s.CareInsurance, s.Pension, 0, s.EmploymentInsurance, s.SocialInsuranceTotal, s.TaxableAmount, s.IncomeTax,
                    0, s.ResidentTax, 0, 0, 0, 0, 0, 0, 0, 0,
                    0, 0, s.TotalDeduction, s.NetPay, s.PayDate.ToString("yyyy/MM/dd"), "振込",
                });
            }
            return CsvText.Write(list);
        }

        /// <summary>スタッフナビゲーターの出力を想定し、Shift_JISでCSVを作成する</summary>
        public static byte[] ToShiftJis(string csv) => Encoding.GetEncoding(932).GetBytes(csv);

        public static string SampleTimesheetFileName => $"タイムシート_{LatestYear}{LatestMonth:00}_横浜港北セルフSS.csv";

        /// <summary>サンプル：10月から始まった受注（横浜港北セルフSS A勤・B勤）の10月分タイムシート</summary>
        public static byte[] SampleTimesheetCsv()
        {
            var records = CreateTimesheet(CreateStaff(), CreateOrders(), LatestYear, LatestMonth, true).Where(r => SampleOrderNos.Contains(r.OrderNo));
            return ToShiftJis(TimesheetCsv(records));
        }

        public static string SamplePayrollFileName => $"{(LatestYear + (LatestMonth == 12 ? 1 : 0)) % 100}.{LatestMonth % 12 + 1}支給給与計算書.csv";

        /// <summary>サンプル：最新の就業月（10月）分の給与計算書（11月支給）。現在の勤務表（補正後）から作成する</summary>
        public byte[] SamplePayrollCsv()
        {
            var view = labor.Build($"{LatestYear}-{LatestMonth:00}");
            var settings = repo.GetSettings();
            var rows = view.Staff.Select(s => (s.Staff, LaborCalculator.Estimate(s, settings, view.PayDate, view.FirstDay, view.LastDay)));
            return ToShiftJis(PayrollCsv(rows));
        }

        public static byte[] SampleStaffCsv() => ToShiftJis(StaffCsv(CreateStaff()));

        public void EnsureSeeded()
        {
            if (!repo.IsInitialized)
            {
                // 形式の古いデータが残っている場合も作り直す
                repo.Clear();
                Seed();
            }
        }

        public void Reset(string user)
        {
            repo.Clear();
            Seed();
            repo.AddLog(user, "設定", "デモデータを初期状態に戻しました");
        }

        private void Seed()
        {
            repo.SaveSettings(new LaborSettings());
            var orders = CreateOrders();
            repo.SaveOrders(orders);
            var staff = CreateStaff();
            var settings = repo.GetSettings();

            Import("staff 全データ_26.10.01時点.csv", ToShiftJis(StaffCsv(staff)), At(2026, 10, 1, 8, 30));

            // 2025年12月～2026年10月の就業分（タイムシート）と、2026年1月～10月支給分（給与計算書）
            var revoked = false;
            for (var month = new DateOnly(2025, 12, 1); month <= new DateOnly(LatestYear, LatestMonth, 1); month = month.AddMonths(1))
            {
                var latest = (month.Year, month.Month) == (LatestYear, LatestMonth);
                var key = month.ToString("yyyy-MM");
                var next = month.AddMonths(1);
                var records = CreateTimesheet(staff, orders, month.Year, month.Month, latest).Where(r => !latest || !SampleOrderNos.Contains(r.OrderNo));
                var importAt = latest ? At(2026, 10, 31, 18, 5) : At(next.Year, next.Month, 2, 9, 10);
                Import($"タイムシート_{month:yyyyMM}.csv", ToShiftJis(TimesheetCsv(records)), importAt);
                if (latest)
                {
                    break;
                }

                // 締め済みの月の要確認データは補正済み（担当者が根拠資料を確認して補正）
                var view = labor.Build(key);
                var minute = 0;
                foreach (var (staffMonth, sheet, day) in Alerts(view))
                {
                    AddSeedCorrection(view, staffMonth, sheet, day, importAt.AddHours(4).AddMinutes(minute += 7));
                }
                // 9月分：誤った補正を取り消した例
                if (month.Month == 9 && !revoked)
                {
                    view = labor.Build(key);
                    var s = view.Staff[3];
                    var sheet = s.Sheets[0];
                    var target = sheet.Days.First(d => d.Effective?.Kind == DayKind.Work && !d.IsCorrected && d.Warnings.Count == 0);
                    var values = target.Effective! with { End = target.Effective!.End + 30 };
                    var at = importAt.AddDays(1).AddHours(2);
                    var wrong = AddCorrection(view, s, sheet, target, values, "店舗責任者より終業時刻の申告あり", "店舗日報（9月分）", StaffUserName, at);
                    const string revokeReason = "日報を再確認した結果、取込データの終業時刻が正しかったため";
                    repo.RevokeCorrection(wrong.Id, AdminName, revokeReason, at.AddDays(1));
                    repo.AddLog(AdminName, "補正取消", $"{wrong.StaffName}（{wrong.EmployeeNo}）{wrong.Date:yyyy/MM/dd} の補正を取り消しました（理由：{revokeReason}）", at.AddDays(1));
                    revoked = true;
                }

                // 補正後の勤怠をスタッフナビゲーターに戻して給与計算し、給与計算書を出力した想定
                view = labor.Build(key);
                var payroll = view.Staff.Select(x => (x.Staff, LaborCalculator.Estimate(x, settings, view.PayDate, view.FirstDay, view.LastDay)));
                var payAt = At(next.Year, next.Month, Math.Min(20, view.PayDate.Day - 2), 15, 30);
                repo.AddLog(StaffUserName, "資料出力", $"スタッフナビゲーター取込用の勤怠データCSVを出力しました（{Fmt.Month(key)}、全就業先、{view.Staff.Count}名、補正{view.CorrectionCount}件反映）", importAt.AddDays(2));
                Import($"{view.PayDate.Year % 100}.{view.PayDate.Month}支給給与計算書.csv", ToShiftJis(PayrollCsv(payroll)), payAt);
            }

            repo.AddLog(AdminName, "資料出力", "勤務表（出勤簿）を出力しました（2026年9月、全就業先、32名）", At(2026, 10, 22, 16, 2));
            repo.AddLog(AdminName, "資料出力", "賃金台帳を出力しました（2026年、全就業先、32名）", At(2026, 10, 22, 16, 5));

            // 前払い（9月・10月・当月の申請。当月分は現在時刻までの申請と、承認待ち2件）
            repo.SaveAdvanceSettings(new AdvanceSettings());
            advance.SeedHistory(Clock.Now);
            repo.MarkInitialized();
        }

        private void Import(string fileName, byte[] bytes, DateTimeOffset at)
        {
            var pending = importer.Parse(fileName, bytes, AdminName);
            importer.Commit(pending, AdminName, at);
        }

        private static IEnumerable<(StaffMonth Staff, OrderSheet Sheet, DayEntry Day)> Alerts(MonthView view) =>
            view.Staff.SelectMany(s => s.Sheets.SelectMany(sheet => sheet.Days.Where(d => d.Warnings.Count > 0).Select(d => (s, sheet, d)))).ToList();

        private void AddSeedCorrection(MonthView view, StaffMonth staffMonth, OrderSheet sheet, DayEntry day, DateTimeOffset at)
        {
            var before = day.Effective!;
            AttendanceValues after;
            string reason;
            string evidence;
            if (before.End == null)
            {
                after = before with { End = sheet.Order.ShiftEnd };
                reason = "終業の打刻漏れ。店舗日報および本人申告により終業時刻を確認";
                evidence = "店舗日報・本人申告書";
            }
            else if (before.Start == null)
            {
                after = before with { Start = sheet.Order.ShiftStart };
                reason = "始業の打刻漏れ。シフト表および店舗責任者の確認により始業時刻を確認";
                evidence = "シフト表・店舗責任者確認";
            }
            else
            {
                after = before with { BreakMinutes = 60 };
                reason = "休憩の取得実績を店舗責任者に確認。勤怠端末の休憩入力漏れのため修正";
                evidence = "店舗責任者ヒアリング記録";
            }
            AddCorrection(view, staffMonth, sheet, day, after, reason, evidence, StaffUserName, at);
        }

        private Correction AddCorrection(MonthView view, StaffMonth staffMonth, OrderSheet sheet, DayEntry day, AttendanceValues after,
            string reason, string evidence, string user, DateTimeOffset at)
        {
            var correction = new Correction
            {
                Id = $"C{at:yyyyMMddHHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
                BatchId = sheet.BatchId,
                Month = view.Month,
                EmployeeNo = staffMonth.Staff.EmployeeNo,
                StaffName = staffMonth.Staff.Name,
                OrderNo = sheet.Order.OrderNo,
                OrderName = sheet.Order.Name,
                Date = day.Date,
                Before = day.Effective,
                After = after,
                Reason = reason,
                Evidence = evidence,
                CorrectedBy = user,
                CorrectedAt = at,
            };
            repo.AddCorrection(correction);
            repo.AddLog(user, "補正", $"{correction.StaffName}（{correction.EmployeeNo}）{correction.Date:yyyy/MM/dd} {sheet.Order.Name}：" +
                $"{correction.Before?.Describe()} → {after.Describe()}（理由：{reason}）", at);
            return correction;
        }

        private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
            new(year, month, day, hour, minute, 0, TimeSpan.FromHours(9));

        /// <summary>スタッフ全データCSVの項目（実データと同じ202項目・並び順）</summary>
        public static readonly string[] StaffFullHeaders = (
            "ｽﾀｯﾌ番号,登録日,検索対象,ｽﾀｯﾌ区分,氏名(ｶﾅ),氏名(漢字),旧姓(漢字),性別,配偶者,子供,生年月日,年齢,郵便番号,住所1,住所2,住所3,沿線1,沿線2,最寄駅," +
            "電話番号,電話番号メモ,携帯番号,携帯番号メモ,緊急連絡先,緊急連絡先メモ,ﾒｰﾙｱﾄﾞﾚｽ,携帯ﾒｰﾙ,入社日,退職,退職日,担当者,ﾗﾝｸ,評価1,評価2,評価3,評価4,評価5," +
            "業務実績1,業務実績2,業務実績3,業務実績4,業務実績5,所持資格1,資格取得年月1,所持資格2,資格取得年月2,所持資格3,資格取得年月3,所持資格4,資格取得年月4," +
            "所持資格5,資格取得年月5,特技1,特技2,特技3,特技4,特技5,稼働実績,初回稼働日,有給付与日数,有給使用日数,有給使用時間,年間有給使用時間,年間有給使用限度日数," +
            "１日あたり有給時間,有給計算方法,有給判定基準,直近付与日,直近付与,支払形態,給与締日1,給与支払日1,給与締日2,給与支払日2,支払先,銀行番号,銀行名,支店番号," +
            "支店名,銀行区分,口座番号,口座名義人,振込元,雇用形態,厚生年金加入状況,厚生年金等級,厚生年金被保険者整理番号,厚生年金基礎番号,厚生年金取得日,厚生年金喪失日," +
            "厚生年金未加入理由,厚生年金基金加入状況,健康保険加入状況,健康保険等級,健康保険被保険者整理番号,健康保険取得日,健康保険喪失日,健康保険未加入理由,介護保険," +
            "保険料率,雇用保険加入状況,雇用保険被保険者番号,雇用保険取得日,雇用保険喪失日,雇用保険未加入理由,税区分,税区分コメント,扶養家族人数,住民税区分," +
            "住民税(6月分),住民税(7月以降),本人−配偶者あり,本人−一般障害者,本人−特別障害者,本人−寡婦,本人−ひとり親,本人−勤労学生,本人−死亡退職,本人−災害者," +
            "本人−外国人,配偶者−一般,配偶者−老人,配偶者−同居特障一般,配偶者−同居特障老人,扶養−年少,扶養−一般,扶養−特定,扶養−同居老親等,扶養−その他老人," +
            "扶養−同居特障年少,扶養−同居特障一般,扶養−同居特障特定,扶養−同居特障老親等,扶養−同居特障他老人,障害者−一般,障害者−特別,障害者−同居特障,年調なし," +
            "他住所−住民税納付先,他住所−年末調整,他住所−郵便番号,他住所−住所1,他住所−住所2,他住所−住所3,源泉徴収票−摘要欄に使用する,源泉徴収票−摘要1," +
            "源泉徴収票−摘要2,源泉徴収票−摘要3,60歳以上の定年退職者,日雇派遣の例外−要件,日雇派遣の例外−確認書類,契約期間,お仕事情報,希望派遣期間,希望業務1," +
            "希望業務2,希望業務3,希望勤務地1,希望勤務地2,希望通勤時間,希望派遣開始日,希望時給,就業条件1,就業条件2,就業条件3,就業条件4,就業条件5,募集媒体,募集日付," +
            "ﾒﾓ,最終学歴,健康診断−検索対象,健康診断−受診日,健康診断−備考,引当メモ,終了日,所属会社,就業先,他社終了予定,退職理由−要因,退職理由−事由,週の所定労働時間," +
            "雇用見込期間−契約期間,雇用見込期間−開始日,雇用見込期間−終了日,雇用見込区分,就業継続,e-naviアップ,e-naviログイン,e-naviメール配信,e-naviメール配信先," +
            "外国人雇用−国籍地域,外国人雇用−氏名フルネーム,外国人雇用−日本語レベル,外国人雇用−在留カード番号,外国人雇用−在留期限,外国人雇用−上陸許可日," +
            "外国人雇用−在留資格,外国人雇用−就労制限の有無,外国人雇用−備考,運転免許−種類,運転免許−期限").Split(',');
    }
}
