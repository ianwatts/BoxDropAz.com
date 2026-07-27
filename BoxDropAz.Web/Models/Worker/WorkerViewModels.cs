using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Worker;

public sealed class ManifestViewModel
{
    public DateOnly Date { get; set; }

    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public List<RentalOrder> Deliveries { get; set; } = new();

    public List<RentalOrder> Pickups { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public int TotalCrates => Deliveries.Sum(o => o.CrateCount);

    public int TotalDollies => Deliveries.Sum(o => o.DollyCount);

    public int DeliveriesRemaining => Deliveries.Count(o => o.DeliveredAtUtc is null);

    public int PickupsRemaining => Pickups.Count(o => o.PickedUpAtUtc is null);
}

public sealed class WorkerOrderViewModel
{
    public required RentalOrder Order { get; set; }

    public Region? Region { get; set; }

    /// <summary>True when this stop is a pickup rather than a delivery, which changes the actions.</summary>
    public bool IsPickup { get; set; }

    public DateOnly ManifestDate { get; set; }
}
