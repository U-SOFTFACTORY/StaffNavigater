namespace StaffNavi.Services
{
    public class DemoOptions
    {
        /// <summary>デモ環境の公開期限。これを過ぎると全画面が公開終了表示になる。</summary>
        public DateTimeOffset? ExpiresAt { get; set; }

        public List<DemoUser> Users { get; set; } = [];
    }

    public class DemoUser
    {
        public string LoginId { get; set; } = "";
        public string Password { get; set; } = "";
        public string Name { get; set; } = "";
        public string Role { get; set; } = "";
    }

    public static class Clock
    {
        private static readonly TimeSpan Jst = TimeSpan.FromHours(9);

        public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Jst);
    }

    public static class Fmt
    {
        private static readonly string[] Weekdays = ["日", "月", "火", "水", "木", "金", "土"];

        public static string Weekday(DateOnly date) => Weekdays[(int)date.DayOfWeek];

        public static string Day(DateOnly date) => $"{date.Month}/{date.Day}({Weekday(date)})";

        public static string Hours(int minutes) => minutes == 0 ? "-" : $"{minutes / 60}:{minutes % 60:00}";

        public static string Yen(long value) => value.ToString("#,0");

        public static string Time(TimeOnly? time) => time?.ToString("HH:mm") ?? "";

        /// <summary>0:00からの分を“時:分”で表す（スタッフナビゲーターと同じく、日をまたぐ時刻は 32:00 のように表す）</summary>
        public static string Clock(int? minutes) => minutes is { } m ? $"{m / 60}:{m % 60:00}" : "";

        /// <summary>“時:分”（例：6:26、32:00）を0:00からの分にする</summary>
        public static bool TryParseClock(string? text, out int minutes)
        {
            minutes = 0;
            var parts = (text ?? "").Trim().Split(':');
            if (parts.Length is < 2 or > 3 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h < 0 || h >= 48 || m is < 0 or > 59)
            {
                return false;
            }
            minutes = h * 60 + m;
            return true;
        }

        /// <summary>“時間:分”形式の時間（例：121:00、31:00:00）を分にする（給与計算書CSVの時間項目）</summary>
        public static int ParseDuration(string? text)
        {
            var parts = (text ?? "").Trim().Split(':');
            return parts.Length >= 2 && int.TryParse(parts[0], out var h) && int.TryParse(parts[1], out var m) ? h * 60 + m : 0;
        }

        /// <summary>勤務表の時間表示（0は空欄）</summary>
        public static string HoursOrBlank(int minutes) => minutes == 0 ? "" : $"{minutes / 60}:{minutes % 60:00}";

        /// <summary>日数（半日を含む）</summary>
        public static string Days(decimal days) => days % 1 == 0 ? $"{days:0}" : $"{days:0.0}";

        public static string DateTime(DateTimeOffset value) => value.ToString("yyyy/MM/dd HH:mm");

        public static string Month(string month) => $"{month[..4]}年{int.Parse(month[5..])}月";
    }
}
