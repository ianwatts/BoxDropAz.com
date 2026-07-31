using System.ComponentModel.DataAnnotations;
using BoxDropAz.Core.Models.Billing;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Web.Models.Admin;

namespace BoxDropAz.Web.Models.SaaSAdmin;

/// <summary>One region's contribution to the platform rollup.</summary>
public sealed class RegionRollup
{
    public required Region Region { get; set; }

    public int RevenueThisMonthCents { get; set; }

    public int RevenueLastMonthCents { get; set; }

    public int OrdersThisMonth { get; set; }

    public int ActiveRentals { get; set; }

    public int GiftOrdersThisMonth { get; set; }

    public int PackageCount { get; set; }

    public int ZoneCount { get; set; }
}

public sealed class PlatformDashboardViewModel
{
    public List<RegionRollup> Regions { get; set; } = new();

    public List<RevenuePoint> MonthlyRevenue { get; set; } = new();

    /// <summary>Region name to that region's 12-month series, for the stacked comparison chart.</summary>
    public Dictionary<string, List<RevenuePoint>> RevenueByRegion { get; set; } = new();

    public int TotalRevenueThisMonthCents => Regions.Sum(r => r.RevenueThisMonthCents);

    public int TotalRevenueLastMonthCents => Regions.Sum(r => r.RevenueLastMonthCents);

    public int TotalOrdersThisMonth => Regions.Sum(r => r.OrdersThisMonth);

    public int TotalActiveRentals => Regions.Sum(r => r.ActiveRentals);

    public int ActiveSubscriptions { get; set; }

    public int MonthlyRecurringCents { get; set; }

    public int OutstandingCreditCents { get; set; }

    public int UserCount { get; set; }

    public double? RevenueChangePercent =>
        TotalRevenueLastMonthCents == 0
            ? null
            : (TotalRevenueThisMonthCents - TotalRevenueLastMonthCents) / (double)TotalRevenueLastMonthCents * 100;
}

/// <summary>Editable shape of a region. Zones and fees are edited as a block on the same form.</summary>
public sealed class RegionEditModel
{
    public string? Id { get; set; }

    [Required]
    [Display(Name = "Region name")]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Display(Name = "URL slug")]
    [RegularExpression("^[a-z0-9-]+$", ErrorMessage = "Use lowercase letters, numbers, and hyphens only.")]
    [StringLength(60)]
    public string Slug { get; set; } = string.Empty;

    [Display(Name = "Description")]
    [StringLength(600)]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Time zone")]
    public string TimeZoneId { get; set; } = "US/Arizona";

    [Display(Name = "Support phone")]
    public string SupportPhone { get; set; } = string.Empty;

    [Display(Name = "Accepting bookings")]
    public bool IsActive { get; set; } = true;

    public List<ZoneEditModel> Zones { get; set; } = new();

    [Display(Name = "Tote/lid set replacement")]
    [Range(0, 100000)]
    public int CrateReplacementCents { get; set; } = 4000;

    [Display(Name = "Dolly replacement")]
    [Range(0, 100000)]
    public int DollyReplacementCents { get; set; } = 9500;

    [Display(Name = "Missed pickup")]
    [Range(0, 100000)]
    public int MissedPickupCents { get; set; } = 2500;

    [Display(Name = "Deep clean, per tote")]
    [Range(0, 100000)]
    public int DeepCleanPerCrateCents { get; set; } = 300;

    public static RegionEditModel FromRegion(Region region) => new()
    {
        Id = region.Id,
        Name = region.Name,
        Slug = region.Slug,
        Description = region.Description,
        TimeZoneId = region.TimeZoneId,
        SupportPhone = region.SupportPhone,
        IsActive = region.IsActive,
        Zones = region.DeliveryZones.Select(z => new ZoneEditModel
        {
            Name = z.Name,
            Cities = z.Cities,
            ZipCodes = string.Join(", ", z.ZipCodes),
            SurchargeCents = z.SurchargeCents
        }).ToList(),
        CrateReplacementCents = region.DamageFees.CrateReplacementCents,
        DollyReplacementCents = region.DamageFees.DollyReplacementCents,
        MissedPickupCents = region.DamageFees.MissedPickupCents,
        DeepCleanPerCrateCents = region.DamageFees.DeepCleanPerCrateCents
    };
}

public sealed class ZoneEditModel
{
    [Display(Name = "Zone name")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Cities covered")]
    public string Cities { get; set; } = string.Empty;

    /// <summary>Comma or whitespace separated ZIPs; parsed on save.</summary>
    [Display(Name = "ZIP codes")]
    public string ZipCodes { get; set; } = string.Empty;

    [Display(Name = "Surcharge")]
    [Range(0, 100000)]
    public int SurchargeCents { get; set; }
}

public sealed class RegionListViewModel
{
    public List<Region> Regions { get; set; } = new();

    public Dictionary<string, int> PackageCounts { get; set; } = new();

    public Dictionary<string, int> OrderCounts { get; set; } = new();
}

public sealed class PackageListViewModel
{
    public required Region Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public List<CratePackage> Packages { get; set; } = new();
}

public sealed class PackageEditModel
{
    public string? RegionId { get; set; }

    /// <summary>Empty on create; the slug is derived from the name and then frozen.</summary>
    public string? PackageId { get; set; }

    [Required]
    [Display(Name = "Package name")]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "Subtitle")]
    [StringLength(160)]
    public string Subtitle { get; set; } = string.Empty;

    [Display(Name = "27-gallon totes with lids")]
    [Range(1, 500)]
    public int CrateCount { get; set; } = 10;

    [Display(Name = "Dollies")]
    [Range(0, 50)]
    public int DollyCount { get; set; }

    [Display(Name = "Base price (1 week)")]
    [Range(0, 1000000)]
    public int BasePriceCents { get; set; }

    [Display(Name = "Each extra week")]
    [Range(0, 1000000)]
    public int ExtraWeekPriceCents { get; set; }

    [Display(Name = "What's included")]
    public string IncludedItems { get; set; } = string.Empty;

    [Display(Name = "Ribbon")]
    [StringLength(40)]
    public string? Badge { get; set; }

    [Display(Name = "Sort order")]
    public int SortOrder { get; set; }

    [Display(Name = "Bookable")]
    public bool IsActive { get; set; } = true;

    public static PackageEditModel FromPackage(CratePackage package) => new()
    {
        RegionId = package.RegionId,
        PackageId = package.PackageId,
        Name = package.Name,
        Subtitle = package.Subtitle,
        CrateCount = package.CrateCount,
        DollyCount = package.DollyCount,
        BasePriceCents = package.BasePriceCents,
        ExtraWeekPriceCents = package.ExtraWeekPriceCents,
        IncludedItems = string.Join("\n", package.IncludedItems),
        Badge = package.Badge,
        SortOrder = package.SortOrder,
        IsActive = package.IsActive
    };
}

public sealed class StripeEventsViewModel
{
    public List<StripeEventRecord> Events { get; set; } = new();

    public string? TypeFilter { get; set; }

    public string? OutcomeFilter { get; set; }

    public IReadOnlyList<string> KnownTypes { get; set; } = Array.Empty<string>();
}
