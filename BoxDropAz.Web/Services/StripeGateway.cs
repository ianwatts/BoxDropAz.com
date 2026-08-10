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

    /// <inheritdoc />
    public bool IsCollectTaxEnabled => _config.GetValue("Stripe:CollectTax", false);

    public async Task<string> EnsureCustomerAsync(ApplicationUser user, CancellationToken ct = default)
    {
        EnsureConfigured();

        if (!string.IsNullOrWhiteSpace(user.StripeCustomerId))
        {
            try
            {
                // Confirm the id still exists on this Stripe account (keys/account switches
                // leave stale cus_ ids that break every subsequent Checkout call).
                await new CustomerService().GetAsync(user.StripeCustomerId, cancellationToken: ct);
                return user.StripeCustomerId;
            }
            catch (StripeException ex) when (
                ex.StripeError?.Code == "resource_missing"
                || ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning(
                    ex,
                    "Stale Stripe customer {CustomerId} for user {UserId}; creating a new one",
                    user.StripeCustomerId,
                    user.Id);
                user.StripeCustomerId = null;
            }
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
        string returnUrl,
        IDictionary<string, string> metadata,
        CheckoutTaxAddress destinationAddress,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var collectTax = IsCollectTaxEnabled;

        // Destination sourcing: Stripe Tax uses Customer.Shipping (or a collected shipping
        // address) for physical goods. Pin the delivery address before creating the session.
        if (collectTax)
        {
            await ApplyDestinationShippingAsync(customerId, destinationAddress, ct);
        }

        var rentalLines = lines.Where(l => l.Kind != CheckoutLineKind.Shipping).ToList();
        var freightCents = lines
            .Where(l => l.Kind == CheckoutLineKind.Shipping)
            .Sum(l => l.UnitAmountCents * l.Quantity);
        var freightLabel = BuildFreightLabel(lines.Where(l => l.Kind == CheckoutLineKind.Shipping).ToList());

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            UiMode = "embedded_page",
            IntegrationIdentifier = "boxdropaz_rental_nqzptkfw",
            Customer = customerId,
            ClientReferenceId = clientReferenceId,
            ReturnUrl = returnUrl,
            LineItems = rentalLines.Select(ToLineItem).ToList(),
            Metadata = new Dictionary<string, string>(metadata),
            // Arizona TPT when CollectTax is on; paused via Stripe:CollectTax=false.
            AutomaticTax = new SessionAutomaticTaxOptions { Enabled = collectTax },
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                // Keeps the card usable for extensions and damage fees without another checkout.
                SetupFutureUsage = "off_session",
                Metadata = new Dictionary<string, string>(metadata)
            }
        };

        // Shipping address collection is required with ShippingOptions, and keeps Tax
        // destination-based when CollectTax is enabled.
        if (collectTax || freightCents > 0)
        {
            options.ShippingAddressCollection = new SessionShippingAddressCollectionOptions
            {
                AllowedCountries = new List<string> { "US" }
            };
            options.CustomerUpdate = new SessionCustomerUpdateOptions
            {
                Shipping = "auto",
                Address = "auto"
            };
        }

        // Stripe forbids shipping PTCs on line_items — freight must use shipping_options /
        // shipping_cost so AZ can treat delivery as exempt freight under TPT rules.
        if (freightCents > 0)
        {
            var shippingRate = new SessionShippingOptionShippingRateDataOptions
            {
                Type = "fixed_amount",
                DisplayName = freightLabel,
                FixedAmount = new SessionShippingOptionShippingRateDataFixedAmountOptions
                {
                    Amount = freightCents,
                    Currency = "usd"
                }
            };

            if (collectTax)
            {
                shippingRate.TaxBehavior = "exclusive";
                shippingRate.TaxCode = StripeTaxCodes.Shipping;
            }

            options.ShippingOptions = new List<SessionShippingOptionOptions>
            {
                new() { ShippingRateData = shippingRate }
            };
        }

        try
        {
            return await new SessionService().CreateAsync(options, cancellationToken: ct);
        }
        catch (StripeException ex) when (IsTaxAddressError(ex))
        {
            _logger.LogWarning(
                ex,
                "Stripe Tax rejected destination address for customer {Customer} ({Zip})",
                customerId,
                destinationAddress.PostalCode);

            throw new StripeTaxAddressException(
                "We couldn't calculate Arizona sales tax for that delivery address. " +
                "Please check the street, city, and ZIP, then try again.",
                ex.StripeError?.Code,
                ex);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe Checkout session create failed for customer {Customer}", customerId);
            throw;
        }
    }

    private static string BuildFreightLabel(IReadOnlyList<CheckoutLine> freightLines)
    {
        if (freightLines.Count == 0)
        {
            return "Delivery";
        }

        if (freightLines.Count == 1)
        {
            return freightLines[0].Name;
        }

        return "Delivery & pickup surcharges";
    }

    /// <summary>
    /// Writes the rental delivery address onto the Stripe Customer so Automatic Tax uses
    /// destination sourcing (Arizona TPT) rather than the business head-office address alone.
    /// </summary>
    private async Task ApplyDestinationShippingAsync(
        string customerId,
        CheckoutTaxAddress address,
        CancellationToken ct)
    {
        try
        {
            await new CustomerService().UpdateAsync(customerId, new CustomerUpdateOptions
            {
                Shipping = new ShippingOptions
                {
                    Name = address.Name,
                    Address = new AddressOptions
                    {
                        Line1 = address.Line1,
                        Line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : address.Line2,
                        City = address.City,
                        State = address.State,
                        PostalCode = address.PostalCode,
                        Country = address.Country
                    }
                },
                // Prefer the same destination for billing when Tax falls back from shipping.
                Address = new AddressOptions
                {
                    Line1 = address.Line1,
                    Line2 = string.IsNullOrWhiteSpace(address.Line2) ? null : address.Line2,
                    City = address.City,
                    State = address.State,
                    PostalCode = address.PostalCode,
                    Country = address.Country
                }
            }, cancellationToken: ct);
        }
        catch (StripeException ex) when (IsTaxAddressError(ex))
        {
            throw new StripeTaxAddressException(
                "That delivery address doesn't look valid for tax calculation. " +
                "Please fix the street address or ZIP and try again.",
                ex.StripeError?.Code,
                ex);
        }
    }

    private static bool IsTaxAddressError(StripeException ex)
    {
        var code = ex.StripeError?.Code ?? string.Empty;
        var param = ex.StripeError?.Param ?? string.Empty;
        var message = ex.Message ?? string.Empty;

        if (code.Contains("address", StringComparison.OrdinalIgnoreCase)
            || code.Contains("tax_location", StringComparison.OrdinalIgnoreCase)
            || code.Contains("customer_tax", StringComparison.OrdinalIgnoreCase)
            || param.Contains("address", StringComparison.OrdinalIgnoreCase)
            || param.Contains("shipping", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return message.Contains("address", StringComparison.OrdinalIgnoreCase)
               && (message.Contains("tax", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                   || message.Contains("could not", StringComparison.OrdinalIgnoreCase));
    }

    public async Task<Session> CreateSetupSessionAsync(
        string customerId,
        string clientReferenceId,
        string returnUrl,
        IDictionary<string, string> metadata,
        CancellationToken ct = default)
    {
        EnsureConfigured();

        var options = new SessionCreateOptions
        {
            Mode = "setup",
            UiMode = "embedded_page",
            IntegrationIdentifier = "boxdropaz_cardsetup_nqzptkfw",
            Currency = "usd",
            Customer = customerId,
            ClientReferenceId = clientReferenceId,
            ReturnUrl = returnUrl,
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
            UiMode = "embedded_page",
            IntegrationIdentifier = "boxdropaz_subscription_nqzptkfw",
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

    private SessionLineItemOptions ToLineItem(CheckoutLine line)
    {
        var productData = new SessionLineItemPriceDataProductDataOptions
        {
            Name = line.Name,
            Description = string.IsNullOrWhiteSpace(line.Description) ? null : line.Description
        };

        var priceData = new SessionLineItemPriceDataOptions
        {
            Currency = "usd",
            UnitAmount = line.UnitAmountCents,
            ProductData = productData
        };

        if (IsCollectTaxEnabled)
        {
            // US TPT is exclusive — tax is added on top of the quoted rental/freight amounts.
            priceData.TaxBehavior = "exclusive";
            productData.TaxCode = line.TaxCode;
        }

        return new SessionLineItemOptions
        {
            Quantity = line.Quantity,
            PriceData = priceData
        };
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Stripe is not configured. Set Stripe:SecretKey or STRIPE_SECRET_KEY.");
        }
    }
}
