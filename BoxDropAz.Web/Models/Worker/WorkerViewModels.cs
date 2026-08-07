using BoxDropAz.Core.Models.Inventory;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Worker;

public static class ManifestViewModes
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    public static string Normalize(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            Week => Week,
            Month => Month,
            _ => Day
        };
}

public sealed class ManifestViewModel
{
    public DateOnly Date { get; set; }

    /// <summary>Inclusive start of the visible range (same as Date for day view).</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>Inclusive end of the visible range (same as Date for day view).</summary>
    public DateOnly EndDate { get; set; }

    /// <summary>day, week, or month.</summary>
    public string ViewMode { get; set; } = ManifestViewModes.Day;

    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public List<RentalOrder> Deliveries { get; set; } = new();

    public List<RentalOrder> Pickups { get; set; } = new();

    public List<InventoryRecord> RestockTasks { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public bool IsMultiDay => StartDate != EndDate;

    public int TotalTotes => Deliveries.Sum(o => o.CrateCount);

    public int TotalDollies => Deliveries.Sum(o => o.DollyCount);

    public int DeliveriesRemaining => Deliveries.Count(o => o.DeliveredAtUtc is null);

    public int PickupsRemaining => Pickups.Count(o => o.PickedUpAtUtc is null);

    public string RangeLabel => ViewMode switch
    {
        ManifestViewModes.Week => $"{StartDate:MMM d} – {EndDate:MMM d, yyyy}",
        ManifestViewModes.Month => StartDate.ToString("MMMM yyyy"),
        _ => Date.ToString("dddd, MMMM d")
    };
}

public sealed class WorkerOrderViewModel
{
    public required RentalOrder Order { get; set; }

    public Region? Region { get; set; }

    /// <summary>True when this stop is a pickup rather than a delivery, which changes the actions.</summary>
    public bool IsPickup { get; set; }

    public DateOnly ManifestDate { get; set; }

    public string ViewMode { get; set; } = ManifestViewModes.Day;

    public bool ShowStopDate { get; set; }
}
