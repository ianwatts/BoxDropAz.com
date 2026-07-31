namespace BoxDropAz.Core.Services;

public static class DeliveryWindows
{
    public const string AllDay = "*";

    public static readonly string[] Daytime =
    {
        "8:00 AM - 10:00 AM",
        "10:00 AM - 12:00 PM",
        "12:00 PM - 2:00 PM",
        "2:00 PM - 4:00 PM"
    };

    public static readonly string[] Evening =
    {
        "5:00 PM - 7:00 PM",
        "7:00 PM - 9:00 PM"
    };

    public static readonly string[] All =
    {
        "8:00 AM - 10:00 AM",
        "10:00 AM - 12:00 PM",
        "12:00 PM - 2:00 PM",
        "2:00 PM - 4:00 PM",
        "5:00 PM - 7:00 PM",
        "7:00 PM - 9:00 PM"
    };

    public static string Default => Evening[0];

    public static bool IsValid(string? window) => All.Contains(window);

    public static string Normalize(string? window) => IsValid(window) ? window! : Default;

    /// <summary>
    /// Arizona does not observe daylight saving, so "today" for scheduling purposes is always
    /// UTC-7 regardless of season.
    /// </summary>
    public static DateOnly TodayInArizona() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-7));

    /// <summary>Earliest bookable delivery date. Same-day routing is already committed.</summary>
    public static DateOnly EarliestDeliveryDate(int minimumNoticeDays = 3)
        => TodayInArizona().AddDays(Math.Max(0, minimumNoticeDays));

    public static DateOnly LatestDeliveryDate() => TodayInArizona().AddDays(180);
}
