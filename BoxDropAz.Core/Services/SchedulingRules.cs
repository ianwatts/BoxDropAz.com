using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Core.Services;

public static class SchedulingRules
{
    public static IReadOnlyList<string> GetAvailableWindows(
        Region region,
        DateOnly date,
        string operation)
    {
        var settings = region.Scheduling ?? new SchedulingSettings();
        var weekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var source = (operation, weekend) switch
        {
            (ScheduleOperations.Pickup, false) => settings.WeekdayPickupWindows,
            (ScheduleOperations.Pickup, true) => settings.WeekendPickupWindows,
            (_, false) => settings.WeekdayDeliveryWindows,
            _ => settings.WeekendDeliveryWindows
        };

        var dateText = date.ToString("yyyy-MM-dd");
        return source
            .Where(DeliveryWindows.IsValid)
            .Distinct(StringComparer.Ordinal)
            .Where(window => !settings.Blackouts.Any(blackout =>
                blackout.Date == dateText
                && (blackout.Operation == operation || blackout.Operation == ScheduleOperations.Both)
                && (blackout.Window == DeliveryWindows.AllDay || blackout.Window == window)))
            .ToList();
    }

    public static DateOnly EarliestDeliveryDate(Region region)
    {
        var date = DeliveryWindows.EarliestDeliveryDate(region.Scheduling?.MinimumNoticeDays ?? 3);
        var latest = DeliveryWindows.LatestDeliveryDate();
        while (date <= latest
               && GetAvailableWindows(region, date, ScheduleOperations.Delivery).Count == 0)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    public static bool IsWindowAvailable(Region region, DateOnly date, string operation, string window)
        => GetAvailableWindows(region, date, operation).Contains(window, StringComparer.Ordinal);
}
