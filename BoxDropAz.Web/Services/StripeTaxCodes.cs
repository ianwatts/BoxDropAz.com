namespace BoxDropAz.Web.Services;

/// <summary>
/// Stripe Product Tax Codes (PTCs) used for Arizona TPT / destination sourcing.
/// Values must come from Stripe's Tax Codes API — never invent <c>txcd_</c> ids.
/// </summary>
public static class StripeTaxCodes
{
    /// <summary>
    /// General - Tangible Goods (tangible personal property). Closest Stripe PTC for
    /// Arizona personal property rental (ADOR business code 014). Stripe has no dedicated
    /// "equipment rental" PTC; TPP rules still apply at the customer's delivery address.
    /// </summary>
    public const string TangiblePersonalProperty = "txcd_99999999";

    /// <summary>
    /// Shipping. Must be applied via Checkout <c>shipping_options</c> / Tax Calculation
    /// <c>shipping_cost</c> — Stripe rejects this PTC on ordinary line items. Used so AZ TPT
    /// can treat itemized delivery as exempt freight (<c>product_exempt</c>).
    /// </summary>
    public const string Shipping = "txcd_92010001";

    /// <summary>Nontaxable — only for true non-taxable adjustments, not rental inventory.</summary>
    public const string Nontaxable = "txcd_00000000";
}

/// <summary>Classifies a Checkout line for tax coding.</summary>
public enum CheckoutLineKind
{
    /// <summary>Tote rental, extra weeks, add-ons — taxed as tangible personal property.</summary>
    Rental,

    /// <summary>Delivery / pickup zone surcharges — shipping/freight tax treatment.</summary>
    Shipping
}

/// <summary>
/// Delivery (destination) address used for Arizona destination-sourced TPT.
/// Mapped onto the Stripe Customer's shipping details before Checkout.
/// </summary>
public sealed record CheckoutTaxAddress(
    string Name,
    string Line1,
    string? Line2,
    string City,
    string State,
    string PostalCode,
    string Country = "US");

/// <summary>Thrown when Stripe Tax cannot resolve the customer's destination address.</summary>
public sealed class StripeTaxAddressException : Exception
{
    public StripeTaxAddressException(string message, string? stripeCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StripeCode = stripeCode;
    }

    public string? StripeCode { get; }
}
