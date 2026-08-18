using System.ComponentModel.DataAnnotations;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Public;

public class RegionScopedViewModel
{
    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();
}

public sealed class LandingViewModel : RegionScopedViewModel
{
    public List<CratePackage> Packages { get; set; } = new();
}

public sealed class PricingViewModel : RegionScopedViewModel
{
    public List<CratePackage> Packages { get; set; } = new();

    public IReadOnlyList<AddOnOption> AddOns { get; set; } = Array.Empty<AddOnOption>();
}

public sealed class ServiceAreasViewModel : RegionScopedViewModel
{
    /// <summary>ZIP the visitor typed into the coverage checker, if any.</summary>
    public string? CheckedZip { get; set; }

    public Region? MatchedRegion { get; set; }

    public DeliveryZone? MatchedZone { get; set; }

    public bool DidCheck => !string.IsNullOrWhiteSpace(CheckedZip);
}

public sealed class RentalTermsViewModel : RegionScopedViewModel
{
    public DamageFeeSchedule DamageFees { get; set; } = new();
}

public sealed class ContactViewModel
{
    [Required]
    [Display(Name = "Your name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    [Required]
    [Display(Name = "How can we help?")]
    [StringLength(2000, MinimumLength = 10)]
    public string Message { get; set; } = string.Empty;
}

public sealed class ThankYouViewModel
{
    public RentalOrder? Order { get; set; }

    public bool AccountCreated { get; set; }
}
