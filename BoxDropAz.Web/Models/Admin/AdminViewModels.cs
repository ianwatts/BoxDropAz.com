using System.ComponentModel.DataAnnotations;
using BoxDropAz.Core.Models.Inventory;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Services;

namespace BoxDropAz.Web.Models.Admin;

public sealed class InventoryViewModel
{
    public Region? Region { get; set; }
    public List<Region> AllRegions { get; set; } = new();
    public bool CanSwitchRegion { get; set; }
    public InventoryAssessment? Assessment { get; set; }
    public List<InventoryRecord> OpenTasks { get; set; } = new();
}

public sealed class InventoryUpdateModel
{
    [Range(0, 100000)]
    [Display(Name = "Total 27-gallon totes with lids")]
    public int TotalTotes { get; set; }

    [Range(0, 10000)]
    [Display(Name = "Total custom-fit dollies")]
    public int TotalDollies { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Loose colored 3x5 index cards")]
    public int TotalIndexCards { get; set; }

    [Range(0, 100000)]
    [Display(Name = "Loose self-adhesive 3x5 card holders")]
    public int TotalCardHolders { get; set; }
}

public sealed class ScheduleSettingsViewModel
{
    public Region? Region { get; set; }
    public List<Region> AllRegions { get; set; } = new();
    public bool CanSwitchRegion { get; set; }
    public SchedulingSettings Settings { get; set; } = new();
}

public sealed class ScheduleSettingsUpdateModel
{
    [Range(0, 30)]
    [Display(Name = "Minimum booking notice (days)")]
    public int MinimumNoticeDays { get; set; } = 3;

    public List<string> WeekdayDeliveryWindows { get; set; } = new();
    public List<string> WeekdayPickupWindows { get; set; } = new();
    public List<string> WeekendDeliveryWindows { get; set; } = new();
    public List<string> WeekendPickupWindows { get; set; } = new();
}

public sealed class ScheduleBlackoutModel
{
    [Required]
    public string Date { get; set; } = string.Empty;

    [Required]
    public string Operation { get; set; } = ScheduleOperations.Both;

    [Required]
    public string Window { get; set; } = DeliveryWindows.AllDay;

    [StringLength(200)]
    public string? Reason { get; set; }
}

/// <summary>One bucket on the revenue chart.</summary>
public sealed record RevenuePoint(string Label, int RevenueCents, int OrderCount);

public sealed class AdminDashboardViewModel
{
    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public int RevenueThisMonthCents { get; set; }

    public int RevenueLastMonthCents { get; set; }

    public int OrdersThisMonth { get; set; }

    public int ActiveRentals { get; set; }

    public int CratesInTheField { get; set; }

    public int PendingDamageCents { get; set; }

    public int PendingDamageCount { get; set; }

    public int GiftOrdersThisMonth { get; set; }

    public List<RevenuePoint> DailyRevenue { get; set; } = new();

    public List<RevenuePoint> MonthlyRevenue { get; set; } = new();

    public List<RentalOrder> UpcomingDeliveries { get; set; } = new();

    public List<RentalOrder> NeedsAttention { get; set; } = new();

    /// <summary>Month-over-month change, or null when there is no prior month to compare with.</summary>
    public double? RevenueChangePercent =>
        RevenueLastMonthCents == 0
            ? null
            : (RevenueThisMonthCents - RevenueLastMonthCents) / (double)RevenueLastMonthCents * 100;
}

public sealed class AdminOrderListViewModel
{
    public List<RentalOrder> Orders { get; set; } = new();

    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public string? StatusFilter { get; set; }

    public string? Search { get; set; }

    public string? FromDate { get; set; }

    public string? ToDate { get; set; }
}

public sealed class AdminOrderDetailViewModel
{
    public required RentalOrder Order { get; set; }

    public Region? Region { get; set; }

    public ApplicationUser? Customer { get; set; }

    public int? WeeklyPriceCents { get; set; }

    public GiftOrder? Gift { get; set; }

    public OrderEditModel Edit { get; set; } = new();
}

/// <summary>The subset of an order an admin can correct after the fact.</summary>
public sealed class OrderEditModel
{
    [Required]
    [Display(Name = "Delivery date")]
    public string DeliveryDate { get; set; } = string.Empty;

    [Display(Name = "Delivery window")]
    public string DeliveryWindow { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Pickup date")]
    public string PickupDate { get; set; } = string.Empty;

    [Display(Name = "Pickup window")]
    public string PickupWindow { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Street address")]
    public string AddressLine1 { get; set; } = string.Empty;

    [Display(Name = "Apt, suite, unit")]
    public string? AddressLine2 { get; set; }

    [Required]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [Display(Name = "ZIP code")]
    public string Zip { get; set; } = string.Empty;

    [Display(Name = "Customer name")]
    public string CustomerName { get; set; } = string.Empty;

    [Display(Name = "Phone")]
    public string CustomerPhone { get; set; } = string.Empty;

    [Display(Name = "27-gallon totes with lids")]
    [Range(1, 200)]
    public int CrateCount { get; set; }

    [Display(Name = "Custom-fit dollies")]
    [Range(0, 50)]
    public int DollyCount { get; set; }

    public static OrderEditModel FromOrder(RentalOrder order) => new()
    {
        DeliveryDate = order.DeliveryDate,
        DeliveryWindow = order.DeliveryWindow,
        PickupDate = order.PickupDate,
        PickupWindow = order.PickupWindow,
        AddressLine1 = order.DeliveryAddressLine1,
        AddressLine2 = order.DeliveryAddressLine2,
        City = order.DeliveryCity,
        Zip = order.DeliveryZip,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CrateCount = order.CrateCount,
        DollyCount = order.DollyCount
    };
}

public sealed class AdminUserRow
{
    public required ApplicationUser User { get; set; }

    public List<string> Roles { get; set; } = new();

    public int OrderCount { get; set; }

    public bool CanImpersonate { get; set; }
}

public sealed class AdminUserListViewModel
{
    public List<AdminUserRow> Users { get; set; } = new();

    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public string? RoleFilter { get; set; }

    public string? Search { get; set; }

    public IReadOnlyList<string> AssignableRoles { get; set; } = Array.Empty<string>();
}

public sealed class AdminNotificationsViewModel
{
    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public bool CanSwitchRegion { get; set; }

    public List<AdminNotificationTypeRow> Types { get; set; } = new();

    public List<AdminStaffOption> Staff { get; set; } = new();
}

public sealed class AdminNotificationTypeRow
{
    public required string Type { get; set; }

    public required string Label { get; set; }

    public required string Description { get; set; }

    public bool NotifySaaSAdmin { get; set; }

    public bool NotifyRegionalAdmin { get; set; }

    public bool NotifyWorker { get; set; }

    public List<string> ExtraUserIds { get; set; } = new();
}

public sealed class AdminStaffOption
{
    public required string UserId { get; set; }

    public required string DisplayName { get; set; }

    public required string Email { get; set; }

    public List<string> Roles { get; set; } = new();
}
