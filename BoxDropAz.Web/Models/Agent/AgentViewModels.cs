using System.ComponentModel.DataAnnotations;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Agent;

public sealed class AgentDashboardViewModel
{
    public required RealtorSubscription Subscription { get; set; }

    public RealtorPlan? Plan { get; set; }

    public List<GiftOrder> RecentGifts { get; set; } = new();

    public List<CreditLedgerEntry> Ledger { get; set; } = new();

    public int GiftsClaimed { get; set; }

    public int GiftsOutstanding { get; set; }

    public int OutstandingValueCents { get; set; }

    public bool BillingPortalAvailable { get; set; }

    /// <summary>False when the agent has never subscribed, which changes the whole page.</summary>
    public bool HasSubscription => Subscription.PlanId != RealtorPlanId.None;
}

public sealed class SubscribeCheckoutViewModel
{
    public required RealtorPlan Plan { get; set; }

    public required string ClientSecret { get; set; }

    public required string PublishableKey { get; set; }
}

public sealed class GiftFormModel
{
    [Required]
    [Display(Name = "Client name")]
    public string ClientName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [Display(Name = "Client email")]
    public string ClientEmail { get; set; } = string.Empty;

    [Phone]
    [Display(Name = "Client mobile (optional)")]
    public string? ClientPhone { get; set; }

    [Required]
    [Display(Name = "Property address")]
    public string PropertyAddressLine1 { get; set; } = string.Empty;

    [Required]
    [Display(Name = "City")]
    public string PropertyCity { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^\d{5}$", ErrorMessage = "Enter a 5 digit ZIP code.")]
    [Display(Name = "ZIP code")]
    public string PropertyZip { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Closing date")]
    public string ClosingDate { get; set; } = string.Empty;

    /// <summary>Whole dollars, because agents think in dollars and cents invite typos.</summary>
    [Range(GiftFormModel.MinGiftDollars, GiftFormModel.MaxGiftDollars)]
    [Display(Name = "Gift amount")]
    public int GiftAmountDollars { get; set; } = 75;

    [StringLength(400)]
    [Display(Name = "Personal note (optional)")]
    public string? PersonalMessage { get; set; }

    [Display(Name = "Include my co-branded insert with the delivery")]
    public bool IncludeCoBrandingInsert { get; set; } = true;

    public const int MinGiftDollars = 25;
    public const int MaxGiftDollars = 1000;

    public int GiftAmountCents => GiftAmountDollars * 100;
}

public sealed class SendGiftViewModel
{
    public GiftFormModel Form { get; set; } = new();

    public required RealtorSubscription Subscription { get; set; }

    public RealtorPlan? Plan { get; set; }

    public List<CratePackage> Packages { get; set; } = new();

    public Region? Region { get; set; }

    /// <summary>Preset amounts, so the agent can see what their gift actually buys.</summary>
    public List<GiftSuggestion> Suggestions { get; set; } = new();
}

public sealed record GiftSuggestion(int AmountCents, string Label, string Detail, bool Affordable);

public sealed class GiftListViewModel
{
    public List<GiftOrder> Gifts { get; set; } = new();

    public required RealtorSubscription Subscription { get; set; }

    public string? StatusFilter { get; set; }
}
