using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Core.Services;

public sealed class RentalQuote
{
    public int PackageBaseCents { get; init; }

    public int ExtraWeeksCents { get; init; }

    public int ZoneSurchargeCents { get; init; }

    public int AddOnsCents { get; init; }

    public int GiftCreditAppliedCents { get; init; }

    public int RentalWeeks { get; init; }

    public List<AddOnLine> AddOns { get; init; } = new();

    public int SubtotalCents => PackageBaseCents + ExtraWeeksCents + ZoneSurchargeCents + AddOnsCents;

    public int TotalDueCents => Math.Max(0, SubtotalCents - GiftCreditAppliedCents);

    /// <summary>
    /// True when the gift credit covers everything. Stripe rejects a $0 payment, so checkout
    /// switches to a setup-mode session to still capture a card for damages.
    /// </summary>
    public bool IsFullyCoveredByCredit => TotalDueCents == 0;

    /// <summary>Credit the client did not get to use, shown so the trade-off of a smaller bundle is visible.</summary>
    public int UnusedCreditCents { get; init; }
}

/// <summary>
/// Single source of truth for what a rental costs. Both the quote shown in the browser and the
/// amount handed to Stripe are produced here, from server-side inputs only.
/// </summary>
public sealed class PricingService
{
    public const int MinRentalWeeks = 1;
    public const int MaxRentalWeeks = 8;

    public RentalQuote Quote(
        CratePackage package,
        DeliveryZone? zone,
        int rentalWeeks,
        IEnumerable<AddOnLine>? addOns = null,
        int availableGiftCreditCents = 0)
    {
        var weeks = ClampWeeks(rentalWeeks);
        var extraWeeks = weeks - 1;

        var lines = NormalizeAddOns(addOns);
        var addOnTotal = lines.Sum(l => l.TotalCents);

        var subtotal = package.BasePriceCents
                       + (extraWeeks * package.ExtraWeekPriceCents)
                       + (zone?.SurchargeCents ?? 0)
                       + addOnTotal;

        // Credit never pays out as cash, so it is capped at the subtotal.
        var creditApplied = Math.Min(Math.Max(0, availableGiftCreditCents), subtotal);

        return new RentalQuote
        {
            PackageBaseCents = package.BasePriceCents,
            ExtraWeeksCents = extraWeeks * package.ExtraWeekPriceCents,
            ZoneSurchargeCents = zone?.SurchargeCents ?? 0,
            AddOnsCents = addOnTotal,
            GiftCreditAppliedCents = creditApplied,
            UnusedCreditCents = Math.Max(0, availableGiftCreditCents - creditApplied),
            RentalWeeks = weeks,
            AddOns = lines
        };
    }

    /// <summary>Cost of extending an in-flight rental by additional weeks.</summary>
    public int QuoteExtension(CratePackage package, int additionalWeeks)
        => Math.Max(0, additionalWeeks) * package.ExtraWeekPriceCents;

    public static int ClampWeeks(int weeks) => Math.Clamp(weeks, MinRentalWeeks, MaxRentalWeeks);

    /// <summary>
    /// Rebuilds add-on lines from the catalog rather than trusting posted prices, and drops
    /// anything with a bad code or a non-positive quantity.
    /// </summary>
    public static List<AddOnLine> NormalizeAddOns(IEnumerable<AddOnLine>? addOns)
    {
        var result = new List<AddOnLine>();
        if (addOns is null)
        {
            return result;
        }

        foreach (var requested in addOns)
        {
            var option = AddOnCatalog.FromCode(requested.Code);
            if (option is null || requested.Quantity <= 0)
            {
                continue;
            }

            result.Add(new AddOnLine
            {
                Code = option.Code,
                Name = option.Name,
                Quantity = Math.Min(requested.Quantity, option.MaxQuantity),
                UnitAmountCents = option.UnitAmountCents
            });
        }

        return result;
    }
}
