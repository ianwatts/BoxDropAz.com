using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Dashboard;

public sealed class CustomerDashboardViewModel
{
    public List<RentalOrder> Active { get; set; } = new();

    public List<RentalOrder> Past { get; set; } = new();

    public string? CardBrand { get; set; }

    public string? CardLast4 { get; set; }

    public bool HasCardOnFile => !string.IsNullOrWhiteSpace(CardLast4);
}

public sealed class OrderDetailViewModel
{
    public required RentalOrder Order { get; set; }

    public Region? Region { get; set; }

    /// <summary>Null when the bundle has been retired, which disables extending.</summary>
    public int? WeeklyPriceCents { get; set; }

    public bool CanExtend { get; set; }

    public bool CanCancel { get; set; }

    public string? CardBrand { get; set; }

    public string? CardLast4 { get; set; }

    public DateOnly? DeliveryDate => DateOnly.TryParse(Order.DeliveryDate, out var d) ? d : null;

    public DateOnly? PickupDate => DateOnly.TryParse(Order.PickupDate, out var d) ? d : null;
}
