using System.ComponentModel.DataAnnotations;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;

namespace BoxDropAz.Web.Models.Booking;

public sealed class PackageSelectViewModel
{
    public Region? Region { get; set; }

    public List<Region> AllRegions { get; set; } = new();

    public List<CratePackage> Packages { get; set; } = new();

    public string? Zip { get; set; }
}

/// <summary>
/// Carried through Schedule -> Review -> Checkout as hidden fields, so no server-side session
/// state is needed and a user can have two booking tabs open without them colliding.
/// The gift claim flow reuses this form with <see cref="GiftToken"/> populated.
/// </summary>
public class BookingFormModel
{
    [Required]
    public string RegionId { get; set; } = string.Empty;

    [Required]
    public string PackageId { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Delivery date")]
    public string DeliveryDate { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Delivery window")]
    public string DeliveryWindow { get; set; } = DeliveryWindows.Default;

    [Display(Name = "Pickup window")]
    public string PickupWindow { get; set; } = DeliveryWindows.Default;

    [Range(PricingService.MinRentalWeeks, PricingService.MaxRentalWeeks)]
    [Display(Name = "How long do you need them?")]
    public int RentalWeeks { get; set; } = 1;

    [Required]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [Phone]
    [Display(Name = "Mobile phone")]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Street address")]
    public string AddressLine1 { get; set; } = string.Empty;

    [Display(Name = "Apt, suite, unit")]
    public string? AddressLine2 { get; set; }

    [Required]
    [Display(Name = "City")]
    public string City { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Enter a 5 digit ZIP code.")]
    [Display(Name = "ZIP code")]
    public string Zip { get; set; } = string.Empty;

    [Display(Name = "Pick up from the same address")]
    public bool PickupSameAsDelivery { get; set; } = true;

    [Display(Name = "Pickup street address")]
    public string? PickupAddressLine1 { get; set; }

    [Display(Name = "Apt, suite, unit")]
    public string? PickupAddressLine2 { get; set; }

    [Display(Name = "Pickup city")]
    public string? PickupCity { get; set; }

    [Display(Name = "Pickup ZIP code")]
    public string? PickupZip { get; set; }

    [Range(0, 40)]
    [Display(Name = "Extra totes with lids")]
    public int ExtraCrateQty { get; set; }

    [Range(0, 15)]
    [Display(Name = "Wardrobe totes")]
    public int WardrobeCrateQty { get; set; }

    [Display(Name = "Anything the driver should know?")]
    [StringLength(500)]
    public string? DeliveryNotes { get; set; }

    [Display(Name = "I accept the rental agreement")]
    public bool AcceptTerms { get; set; }

    /// <summary>Set only when this booking is redeeming a realtor gift.</summary>
    public string? GiftToken { get; set; }

    public List<AddOnLine> ToAddOnLines()
    {
        var lines = new List<AddOnLine>();

        void Add(AddOnOption option, int quantity)
        {
            if (quantity > 0)
            {
                lines.Add(new AddOnLine
                {
                    Code = option.Code,
                    Name = option.Name,
                    Quantity = quantity,
                    UnitAmountCents = option.UnitAmountCents
                });
            }
        }

        Add(AddOnCatalog.ExtraCrate, ExtraCrateQty);
        Add(AddOnCatalog.WardrobeCrate, WardrobeCrateQty);

        return lines;
    }

    public DateOnly? ParseDeliveryDate()
        => DateOnly.TryParse(DeliveryDate, out var parsed) ? parsed : null;

    /// <summary>Pickup lands one full rental period after delivery.</summary>
    public DateOnly? ComputePickupDate()
    {
        var delivery = ParseDeliveryDate();
        return delivery?.AddDays(RentalTerms.BaseRentalDays * PricingService.ClampWeeks(RentalWeeks));
    }
}

public sealed class ScheduleViewModel
{
    public BookingFormModel Form { get; set; } = new();

    public Region? Region { get; set; }

    public CratePackage? Package { get; set; }

    public List<CratePackage> Packages { get; set; } = new();

    public DeliveryZone? Zone { get; set; }

    public RentalQuote? Quote { get; set; }

    public IReadOnlyList<AddOnOption> AddOns { get; set; } = AddOnCatalog.All;

    public int GiftCreditCents { get; set; }

    public string? GiftingAgentName { get; set; }

    public DateOnly EarliestDeliveryDate { get; set; }

    public int MinimumNoticeDays { get; set; } = 3;

    public IReadOnlyList<string> AvailableDeliveryWindows { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> AvailablePickupWindows { get; set; } = Array.Empty<string>();
}

public sealed class ReviewViewModel
{
    public BookingFormModel Form { get; set; } = new();

    public required Region Region { get; set; }

    public required CratePackage Package { get; set; }

    public DeliveryZone? Zone { get; set; }

    public required RentalQuote Quote { get; set; }

    public DateOnly DeliveryDate { get; set; }

    public DateOnly PickupDate { get; set; }

    public int GiftCreditCents { get; set; }

    public string? GiftingAgentName { get; set; }

    public bool StripeConfigured { get; set; } = true;
}

public sealed class BookingCompleteViewModel
{
    public required RentalOrder Order { get; set; }

    public bool PaymentConfirmed { get; set; }

    public bool AccountCreated { get; set; }
}
