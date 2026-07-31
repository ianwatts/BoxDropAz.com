using BoxDropAz.Core.Services;

namespace BoxDropAz.Core.Models.Regions;

public sealed class SchedulingSettings
{
    public int MinimumNoticeDays { get; set; } = 3;

    public List<string> WeekdayDeliveryWindows { get; set; } = new(DeliveryWindows.Evening);

    public List<string> WeekdayPickupWindows { get; set; } = new(DeliveryWindows.Evening);

    public List<string> WeekendDeliveryWindows { get; set; } = new(DeliveryWindows.Daytime);

    public List<string> WeekendPickupWindows { get; set; } = new(DeliveryWindows.Daytime);

    public List<ScheduleBlackout> Blackouts { get; set; } = new();
}

public sealed class ScheduleBlackout
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Date { get; set; } = string.Empty;

    public string Operation { get; set; } = ScheduleOperations.Both;

    public string Window { get; set; } = DeliveryWindows.AllDay;

    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class ScheduleOperations
{
    public const string Delivery = "delivery";
    public const string Pickup = "pickup";
    public const string Both = "both";

    public static bool IsValid(string? value)
        => value is Delivery or Pickup or Both;
}
