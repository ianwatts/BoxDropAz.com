using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Web.Models.Identity;
using Stripe;
using Stripe.Checkout;

namespace BoxDropAz.Web.Services;

/// <summary>Discriminates checkout sessions so the single webhook endpoint can branch correctly.</summary>
public static class CheckoutKind
{
    public const string RentalOrder = "rental_order";
    public const string GiftSetup = "gift_setup";
    public const string RealtorSubscription = "realtor_subscription";
    public const string PaymentMethodUpdate = "payment_method_update";
}

public sealed record CheckoutLine(string Name, string? Description, int UnitAmountCents, int Quantity);

public interface IStripeGateway
{
    /// <summary>False when no secret key is configured, so callers can degrade instead of throwing.</summary>
    bool IsConfigured { get; }

    /// <summary>Returns the user's Stripe customer id, creating one on first use.</summary>
    Task<string> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct = default);

    /// <summary>
    /// Embedded Checkout in payment mode. Retains the card off-session so extensions and damage
    /// fees can be charged later without the customer present.
    /// </summary>
    Task<Session> CreatePaymentSessionAsync(
        string customerId,
        string clientReferenceId,
        IReadOnlyList<CheckoutLine> lines,
        string returnUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default);

    /// <summary>
    /// Setup mode session, used when a gift credit covers the whole total. Stripe rejects a $0
    /// payment, but the rental agreement still requires a card on file.
    /// </summary>
    Task<Session> CreateSetupSessionAsync(
        string customerId,
        string clientReferenceId,
        string returnUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default);

    /// <summary>Embedded Checkout for the realtor subscription, returning the client secret.</summary>
    Task<Session> CreateSubscriptionSessionAsync(
        string customerId,
        string clientReferenceId,
        string priceId,
        string returnUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default);

    Task<string?> CreateBillingPortalUrlAsync(string customerId, string returnUrl, CancellationToken ct = default);

    Task<Session?> GetSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Reads the card brand and last four from a completed session, for display.</summary>
    Task<(string? PaymentMethodId, string? Brand, string? Last4)> GetSessionPaymentMethodAsync(Session session, CancellationToken ct = default);

    /// <summary>
    /// Charges the stored card while the customer is away. Used for extensions and approved
    /// damage fees.
    /// </summary>
    Task<PaymentIntent> ChargeOffSessionAsync(
        string customerId,
        string paymentMethodId,
        int amountCents,
        string description,
        IDictionary<string, string> metadata,
        CancellationToken ct = default);

    /// <summary>Cancels at period end so the agent keeps the credit they already paid for.</summary>
    Task CancelSubscriptionAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default);

    /// <summary>
    /// The customer's most recent subscription. Used by the webhook when an invoice lands before
    /// the checkout session that would have told us which plan it belongs to.
    /// </summary>
    Task<Subscription?> GetSubscriptionForCustomerAsync(string customerId, CancellationToken ct = default);

    /// <summary>Price id backing a subscription, read from its first item.</summary>
    string? GetPriceId(Subscription subscription);

    /// <summary>When the current paid period ends, for showing a renewal date.</summary>
    DateTime? GetPeriodEnd(Subscription subscription);

    /// <summary>Maps a Stripe price id back to a plan using the configured price id settings.</summary>
    RealtorPlanId ResolvePlanFromPriceId(string? priceId);

    string? GetPriceIdForPlan(RealtorPlanId planId);
}
