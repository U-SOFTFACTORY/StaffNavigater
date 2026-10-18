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

        public static string DateTime(DateTimeOffset value) => value.ToString("yyyy/MM/dd HH:mm");

        public static string Month(string month) => $"{month[..4]}年{int.Parse(month[5..])}月";
    }
}
