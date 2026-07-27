using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// The single Stripe endpoint. Stripe is the authority on whether money moved, so this is what
/// actually confirms orders and grants realtor credit; the browser return pages are a convenience.
/// </summary>
[AllowAnonymous]
[Route("stripe/webhook")]
public sealed class StripeWebhookController : Controller
{
    private readonly IStripeEventStore _events;
    private readonly IStripeGateway _stripe;
    private readonly IOrderService _orders;
    private readonly ISubscriptionService _subscriptions;
    private readonly OrderCheckoutService _checkout;
    private readonly DynamoDbDataHelper _data;
    private readonly IConfiguration _config;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IStripeEventStore events,
        IStripeGateway stripe,
        IOrderService orders,
        ISubscriptionService subscriptions,
        OrderCheckoutService checkout,
        DynamoDbDataHelper data,
        IConfiguration config,
        ILogger<StripeWebhookController> logger)
    {
        _events = events;
        _stripe = stripe;
        _orders = orders;
        _subscriptions = subscriptions;
        _checkout = checkout;
        _data = data;
        _config = config;
        _logger = logger;
    }

    [HttpPost("")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync(ct);
        var signature = Request.Headers["Stripe-Signature"].ToString();
        var secret = _config["Stripe:WebhookSecret"];

        if (string.IsNullOrWhiteSpace(secret))
        {
            // Without a secret we cannot tell a real Stripe call from a forged one, so refuse
            // rather than acting on an unverified payload.
            _logger.LogError("Stripe webhook received but Stripe:WebhookSecret is not configured");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(payload, signature, secret, throwOnApiVersionMismatch: false);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Rejected a Stripe webhook with an invalid signature");
            return BadRequest();
        }

        if (!await _events.TryClaimAsync(stripeEvent.Id, stripeEvent.Type, ct))
        {
            // Already handled. Returning 200 stops Stripe retrying a duplicate forever.
            return Ok();
        }

        try
        {
            var outcome = await DispatchAsync(stripeEvent, ct);
            await _events.MarkProcessedAsync(stripeEvent.Id, outcome.Result, outcome.RelatedId, ct);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe event {EventId} ({EventType}) failed", stripeEvent.Id, stripeEvent.Type);
            await _events.MarkFailedAsync(stripeEvent.Id, ex.Message, ct);

            // Drop the claim so Stripe's retry gets a real second attempt at a transient fault.
            await _events.ReleaseAsync(stripeEvent.Id, ct);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private readonly record struct Outcome(string Result, string? RelatedId = null);

    private async Task<Outcome> DispatchAsync(Event stripeEvent, CancellationToken ct)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                return await HandleCheckoutCompletedAsync((Stripe.Checkout.Session)stripeEvent.Data.Object, ct);

            case "invoice.paid":
                return await HandleInvoicePaidAsync((Invoice)stripeEvent.Data.Object, ct);

            case "customer.subscription.created":
            case "customer.subscription.updated":
                return await HandleSubscriptionChangedAsync((Subscription)stripeEvent.Data.Object, ct);

            case "customer.subscription.deleted":
                return await HandleSubscriptionDeletedAsync((Subscription)stripeEvent.Data.Object, ct);

            case "payment_intent.payment_failed":
                return await HandlePaymentFailedAsync((PaymentIntent)stripeEvent.Data.Object, ct);

            default:
                return new Outcome("ignored");
        }
    }

    // ---------- checkout ----------

    private async Task<Outcome> HandleCheckoutCompletedAsync(Stripe.Checkout.Session session, CancellationToken ct)
    {
        // The event payload carries no expansions, so refetch to read the payment method.
        var full = await _stripe.GetSessionAsync(session.Id, ct) ?? session;
        var metadata = full.Metadata ?? new Dictionary<string, string>();
        metadata.TryGetValue("kind", out var kind);

        switch (kind)
        {
            case CheckoutKind.RentalOrder:
            case CheckoutKind.GiftSetup:
                return await ConfirmRentalAsync(full, metadata, ct);

            case CheckoutKind.RealtorSubscription:
                return await ActivateSubscriptionAsync(full, metadata, ct);

            case CheckoutKind.PaymentMethodUpdate:
                {
                    metadata.TryGetValue("userId", out var userId);
                    var (paymentMethodId, brand, last4) = await _stripe.GetSessionPaymentMethodAsync(full, ct);
                    await _checkout.StoreCardOnUserAsync(userId ?? full.ClientReferenceId ?? "", paymentMethodId, brand, last4, ct);
                    return new Outcome("card_updated", userId);
                }

            default:
                _logger.LogWarning("Checkout session {SessionId} had no recognised kind metadata", full.Id);
                return new Outcome("ignored", full.Id);
        }
    }

    private async Task<Outcome> ConfirmRentalAsync(
        Stripe.Checkout.Session session,
        IDictionary<string, string> metadata,
        CancellationToken ct)
    {
        if (!metadata.TryGetValue("orderId", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            return new Outcome("ignored");
        }

        var order = await _orders.GetAsync(orderId, ct);
        if (order is null)
        {
            _logger.LogWarning("Checkout session {SessionId} referenced unknown order {OrderId}", session.Id, orderId);
            return new Outcome("order_not_found", orderId);
        }

        // False here means the browser already confirmed it on return from Stripe.
        var confirmed = await _checkout.ConfirmFromSessionAsync(order, session, ct);
        return new Outcome(confirmed ? "order_confirmed" : "order_already_confirmed", orderId);
    }

    // ---------- realtor subscriptions ----------

    private async Task<Outcome> ActivateSubscriptionAsync(
        Stripe.Checkout.Session session,
        IDictionary<string, string> metadata,
        CancellationToken ct)
    {
        metadata.TryGetValue("userId", out var userId);
        userId ??= session.ClientReferenceId;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new Outcome("ignored", session.Id);
        }

        metadata.TryGetValue("regionId", out var regionId);
        var subscription = await _subscriptions.GetOrCreateAsync(userId, regionId ?? string.Empty, ct);

        subscription.StripeCustomerId = session.CustomerId;
        subscription.StripeSubscriptionId = session.SubscriptionId;

        var planId = ResolvePlanFromMetadata(metadata);
        if (planId == RealtorPlanId.None && !string.IsNullOrWhiteSpace(session.CustomerId))
        {
            var stripeSubscription = await _stripe.GetSubscriptionForCustomerAsync(session.CustomerId, ct);
            if (stripeSubscription is not null)
            {
                planId = _stripe.ResolvePlanFromPriceId(_stripe.GetPriceId(stripeSubscription));
                subscription.CurrentPeriodEndUtc = _stripe.GetPeriodEnd(stripeSubscription);
            }
        }

        ApplyPlan(subscription, planId);
        subscription.Status = "active";

        await _subscriptions.SaveAsync(subscription, ct);

        _logger.LogInformation("Activated {Plan} subscription for realtor {UserId}", subscription.PlanName, userId);
        return new Outcome("subscription_activated", userId);
    }

    private async Task<Outcome> HandleInvoicePaidAsync(Invoice invoice, CancellationToken ct)
    {
        var customerId = invoice.CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return new Outcome("ignored", invoice.Id);
        }

        var subscription = await ResolveSubscriptionAsync(customerId, ct);
        if (subscription is null)
        {
            // A one-off invoice, or an agent record we do not own. Nothing to grant.
            return new Outcome("no_subscription", invoice.Id);
        }

        if (subscription.PlanId == RealtorPlanId.None)
        {
            // The invoice beat the checkout session. Read the plan straight from Stripe.
            var stripeSubscription = await _stripe.GetSubscriptionForCustomerAsync(customerId, ct);
            if (stripeSubscription is null)
            {
                return new Outcome("no_subscription", invoice.Id);
            }

            ApplyPlan(subscription, _stripe.ResolvePlanFromPriceId(_stripe.GetPriceId(stripeSubscription)));
            subscription.StripeSubscriptionId = stripeSubscription.Id;
            subscription.CurrentPeriodEndUtc = _stripe.GetPeriodEnd(stripeSubscription);
            subscription.Status = "active";
            await _subscriptions.SaveAsync(subscription, ct);
        }

        if (subscription.PlanId == RealtorPlanId.None)
        {
            _logger.LogWarning("Invoice {InvoiceId} paid but no plan matched a configured price id", invoice.Id);
            return new Outcome("unknown_plan", invoice.Id);
        }

        // Event-level dedup already guarantees this runs once per invoice.
        var granted = await _subscriptions.GrantMonthlyCreditAsync(
            subscription.UserId, subscription.PlanId, invoice.Id, ct);

        return new Outcome(granted ? "credit_granted" : "credit_skipped", subscription.UserId);
    }

    private async Task<Outcome> HandleSubscriptionChangedAsync(Subscription stripeSubscription, CancellationToken ct)
    {
        var subscription = await ResolveSubscriptionAsync(stripeSubscription.CustomerId, ct);
        if (subscription is null)
        {
            return new Outcome("no_subscription", stripeSubscription.Id);
        }

        var planId = _stripe.ResolvePlanFromPriceId(_stripe.GetPriceId(stripeSubscription));
        if (planId != RealtorPlanId.None)
        {
            ApplyPlan(subscription, planId);
        }

        subscription.StripeSubscriptionId = stripeSubscription.Id;
        subscription.StripeCustomerId = stripeSubscription.CustomerId;
        subscription.Status = stripeSubscription.Status;
        subscription.CurrentPeriodEndUtc = _stripe.GetPeriodEnd(stripeSubscription);

        await _subscriptions.SaveAsync(subscription, ct);
        return new Outcome($"subscription_{stripeSubscription.Status}", subscription.UserId);
    }

    private async Task<Outcome> HandleSubscriptionDeletedAsync(Subscription stripeSubscription, CancellationToken ct)
    {
        var subscription = await ResolveSubscriptionAsync(stripeSubscription.CustomerId, ct);
        if (subscription is null)
        {
            return new Outcome("no_subscription", stripeSubscription.Id);
        }

        subscription.Status = "canceled";
        subscription.CurrentPeriodEndUtc = _stripe.GetPeriodEnd(stripeSubscription);

        // Credit already granted stays spendable: the agent paid for it.
        await _subscriptions.SaveAsync(subscription, ct);

        return new Outcome("subscription_canceled", subscription.UserId);
    }

    /// <summary>
    /// Finds our subscription record for a Stripe customer, falling back to the user record when
    /// an invoice arrives before we ever wrote a subscription row.
    /// </summary>
    private async Task<RealtorSubscription?> ResolveSubscriptionAsync(string? customerId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return null;
        }

        var existing = await _subscriptions.GetByStripeCustomerIdAsync(customerId, ct);
        if (existing is not null)
        {
            return existing;
        }

        var user = await _data.GetUserByAttributeAsync("StripeCustomerId", customerId, ct);
        if (user is null)
        {
            return null;
        }

        var created = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);
        created.StripeCustomerId = customerId;
        return created;
    }

    // ---------- failed off-session charges ----------

    private async Task<Outcome> HandlePaymentFailedAsync(PaymentIntent intent, CancellationToken ct)
    {
        var metadata = intent.Metadata ?? new Dictionary<string, string>();
        if (!metadata.TryGetValue("orderId", out var orderId) || string.IsNullOrWhiteSpace(orderId))
        {
            return new Outcome("ignored", intent.Id);
        }

        var order = await _orders.GetAsync(orderId, ct);
        if (order is null)
        {
            return new Outcome("order_not_found", orderId);
        }

        var reason = intent.LastPaymentError?.Message ?? "The card was declined.";

        // Mark whichever off-session charge this intent belongs to, so staff see it needs chasing.
        var damage = order.Damages.FirstOrDefault(d => d.StripePaymentIntentId == intent.Id);
        if (damage is not null)
        {
            damage.Status = DamageChargeStatus.ChargeFailed;
            damage.FailureReason = reason;
        }

        var extension = order.Extensions.FirstOrDefault(e => e.StripePaymentIntentId == intent.Id);
        if (extension is not null)
        {
            extension.Succeeded = false;
            extension.FailureReason = reason;
        }

        if (damage is null && extension is null)
        {
            return new Outcome("no_matching_charge", orderId);
        }

        order.Notes.Add(new OrderNote
        {
            Body = $"Card declined for {Money.Format((int)intent.Amount)}: {reason}",
            AuthorName = "Stripe"
        });

        await _orders.SaveAsync(order, ct);
        return new Outcome("charge_failed", orderId);
    }

    // ---------- helpers ----------

    private static RealtorPlanId ResolvePlanFromMetadata(IDictionary<string, string> metadata)
        => metadata.TryGetValue("planId", out var raw) && Enum.TryParse<RealtorPlanId>(raw, out var parsed)
            ? parsed
            : RealtorPlanId.None;

    private static void ApplyPlan(RealtorSubscription subscription, RealtorPlanId planId)
    {
        var plan = RealtorPlan.FromId(planId);
        if (plan is null)
        {
            return;
        }

        subscription.PlanId = plan.Id;
        subscription.PlanName = plan.Name;
        subscription.MonthlyCreditCents = plan.MonthlyCreditCents;
        subscription.CreditCapCents = plan.CreditCapCents;
        subscription.SeatCount = plan.SeatCount;
        subscription.CoBrandingEnabled = plan.CoBrandingEnabled;
    }
}
