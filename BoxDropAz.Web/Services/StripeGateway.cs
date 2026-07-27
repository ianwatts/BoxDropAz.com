using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Web.Data;
using BoxDropAz.Web.Models.Identity;
using Stripe;
using Stripe.Checkout;

namespace BoxDropAz.Web.Services;

public sealed class StripeGateway : IStripeGateway
{
    private readonly IConfiguration _config;
    private readonly DynamoDbDataHelper _data;
    private readonly ILogger<StripeGateway> _logger;

    public StripeGateway(IConfiguration config, DynamoDbDataHelper data, ILogger<StripeGateway> logger)
    {
        _config = config;
        _data = data;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(StripeConfiguration.ApiKey);

    public async Task<string> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            return user.StripeCustomerId;
        }

        var customer = await new CustomerService().CreateAsync(new CustomerCreateOptions
        {
            Email = user.Email,
            Name = user.FullName,
            Phone = user.PhoneNumber,
            Metadata = new Dictionary<string, string> { ["userId"] = user.Id }
        }, cancellationToken: ct);

        user.StripeCustomerId = customer.Id;

        using var dbContext = _data.CreateContext();
        await dbContext.SaveAsync(user, ct);

        return customer.Id;
    }

    public async Task<Session> CreatePaymentSessionAsync(
        string customerId,
        string clientReferenceId,
        IReadOnlyList<CheckoutLine> lines,
        string successUrl,
        string cancelUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            Customer = customerId,
            ClientReferenceId = clientReferenceId,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            LineItems = lines.Select(ToLineItem).ToList(),
            Metadata = new Dictionary<string, string>(metadata),
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                // Keeps the card usable for extensions and damage fees without another checkout.
                SetupFutureUsage = "off_session",
                Metadata = new Dictionary<string, string>(metadata)
            }
        };

        return await new SessionService().CreateAsync(options, cancellationToken: ct);
    }

    public async Task<Session> CreateSetupSessionAsync(
        string customerId,
        string clientReferenceId,
        string successUrl,
        string cancelUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var options = new SessionCreateOptions
        {
            Mode = "setup",
            Customer = customerId,
            ClientReferenceId = clientReferenceId,
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = new Dictionary<string, string>(metadata),
            SetupIntentData = new SessionSetupIntentDataOptions
            {
                Metadata = new Dictionary<string, string>(metadata)
            }
        };

        return await new SessionService().CreateAsync(options, cancellationToken: ct);
    }

    public async Task<Session> CreateSubscriptionSessionAsync(
        string customerId,
        string clientReferenceId,
        string priceId,
        string returnUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            UiMode = "embedded",
            Customer = customerId,
            ClientReferenceId = clientReferenceId,
            ReturnUrl = returnUrl,
            LineItems = new List<SessionLineItemOptions>
            {
                new() { Price = priceId, Quantity = 1 }
            },
            Metadata = new Dictionary<string, string>(metadata),
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>(metadata)
            }
        };

        return await new SessionService().CreateAsync(options, cancellationToken: ct);
    }

    public async Task<string?> CreateBillingPortalUrlAsync(string customerId, string returnUrl, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(customerId))
        {
            return null;
        }

        try
        {
            var session = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = customerId,
                    ReturnUrl = returnUrl
                }, cancellationToken: ct);

            return session.Url;
        }
        catch (StripeException ex)
        {
            // The portal needs configuring once in the Stripe dashboard; until then, degrade.
            _logger.LogWarning(ex, "Could not create a Stripe billing portal session for {Customer}", customerId);
            return null;
        }
    }

    public async Task<Session?> GetSessionAsync(string sessionId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        try
        {
            return await new SessionService().GetAsync(sessionId, new SessionGetOptions
            {
                Expand = new List<string> { "payment_intent", "setup_intent", "subscription" }
            }, cancellationToken: ct);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not load Stripe session {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<(string? PaymentMethodId, string? Brand, string? Last4)> GetSessionPaymentMethodAsync(Session session, CancellationToken ct = default)
    {
        var paymentMethodId = session.PaymentIntent?.PaymentMethodId ?? session.SetupIntent?.PaymentMethodId;
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            return (null, null, null);
        }

        try
        {
            var method = await new PaymentMethodService().GetAsync(paymentMethodId, cancellationToken: ct);
            return (paymentMethodId, method.Card?.Brand, method.Card?.Last4);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not load payment method {PaymentMethodId}", paymentMethodId);
            return (paymentMethodId, null, null);
        }
    }

    public async Task<PaymentIntent> ChargeOffSessionAsync(
        string customerId,
        string paymentMethodId,
        int amountCents,
        string description,
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        return await new PaymentIntentService().CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = amountCents,
            Currency = "usd",
            Customer = customerId,
            PaymentMethod = paymentMethodId,
            Description = description,
            Confirm = true,
            OffSession = true,
            Metadata = new Dictionary<string, string>(metadata),
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
                // No customer is present to complete a bank redirect.
                AllowRedirects = "never"
            }
        }, cancellationToken: ct);
    }

    public async Task CancelSubscriptionAtPeriodEndAsync(string subscriptionId, CancellationToken ct = default)
    {
        EnsureConfigured();

        // Fully qualified: this class lives alongside our own SubscriptionService.
        await new Stripe.SubscriptionService().UpdateAsync(subscriptionId, new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = true
        }, cancellationToken: ct);
    }

    public async Task<Subscription?> GetSubscriptionForCustomerAsync(string customerId, CancellationToken ct = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(customerId))
        {
            return null;
        }

        try
        {
            var list = await new Stripe.SubscriptionService().ListAsync(new SubscriptionListOptions
            {
                Customer = customerId,
                Limit = 1,
                Status = "all"
            }, cancellationToken: ct);

            return list.Data.FirstOrDefault();
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Could not list subscriptions for customer {Customer}", customerId);
            return null;
        }
    }

    public string? GetPriceId(Subscription subscription)
        => subscription.Items?.Data?.FirstOrDefault()?.Price?.Id;

    public DateTime? GetPeriodEnd(Subscription subscription)
        => subscription.Items?.Data?.FirstOrDefault()?.CurrentPeriodEnd;

    public RealtorPlanId ResolvePlanFromPriceId(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            return RealtorPlanId.None;
        }

        foreach (var plan in RealtorPlan.All)
        {
            if (string.Equals(_config[plan.PriceIdConfigKey], priceId, StringComparison.Ordinal))
            {
                return plan.Id;
            }
        }

        return RealtorPlanId.None;
    }

    public string? GetPriceIdForPlan(RealtorPlanId planId)
    {
        var plan = RealtorPlan.FromId(planId);
        return plan is null ? null : _config[plan.PriceIdConfigKey];
    }

    private static SessionLineItemOptions ToLineItem(CheckoutLine line) => new()
    {
        Quantity = line.Quantity,
        PriceData = new SessionLineItemPriceDataOptions
        {
            Currency = "usd",
            UnitAmount = line.UnitAmountCents,
            ProductData = new SessionLineItemPriceDataProductDataOptions
            {
                Name = line.Name,
                Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description
            }
        }
    };

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Stripe is not configured. Set Stripe:SecretKey or STRIPE_SECRET_KEY.");
        }
    }
}
