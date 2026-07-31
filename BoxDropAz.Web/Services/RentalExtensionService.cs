using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Stripe;

namespace BoxDropAz.Web.Services;

public sealed record ExtensionResult(bool Success, string Message, ExtensionCharge? Charge = null);

/// <summary>
/// Extends an in-flight rental by charging the card the customer left on file. Shared by the
/// customer dashboard and the admin order screen so both produce the same audit trail.
/// </summary>
public sealed class RentalExtensionService
{
    private readonly IOrderService _orders;
    private readonly ICatalogService _catalog;
    private readonly IStripeGateway _stripe;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly PricingService _pricing;
    private readonly OrderNotifier _notifier;
    private readonly InventoryService _inventory;
    private readonly ILogger<RentalExtensionService> _logger;

    public RentalExtensionService(
        IOrderService orders,
        ICatalogService catalog,
        IStripeGateway stripe,
        UserManager<ApplicationUser> userManager,
        PricingService pricing,
        OrderNotifier notifier,
        InventoryService inventory,
        ILogger<RentalExtensionService> logger)
    {
        _orders = orders;
        _catalog = catalog;
        _stripe = stripe;
        _userManager = userManager;
        _pricing = pricing;
        _notifier = notifier;
        _inventory = inventory;
        _logger = logger;
    }

    /// <summary>Weekly price for this order's bundle, or null when the package no longer exists.</summary>
    public async Task<int?> GetWeeklyPriceCentsAsync(RentalOrder order, CancellationToken ct = default)
    {
        var package = await _catalog.GetPackageAsync(order.RegionId, order.PackageId, ct);
        return package?.ExtraWeekPriceCents;
    }

    public async Task<ExtensionResult> ExtendAsync(
        RentalOrder order,
        int additionalWeeks,
        string requestedByUserId,
        CancellationToken ct = default)
    {
        if (additionalWeeks < 1 || additionalWeeks > 8)
        {
            return new ExtensionResult(false, "Choose between 1 and 8 additional weeks.");
        }

        if (!order.IsActiveRental)
        {
            return new ExtensionResult(false, "Only an active rental can be extended.");
        }

        var package = await _catalog.GetPackageAsync(order.RegionId, order.PackageId, ct);
        if (package is null)
        {
            return new ExtensionResult(false, "We couldn't price that extension. Please call us and we'll sort it out.");
        }

        var amountCents = _pricing.QuoteExtension(package, additionalWeeks);
        if (amountCents <= 0)
        {
            return new ExtensionResult(false, "That extension has no cost, so there's nothing to charge.");
        }

        var user = await _userManager.FindByIdAsync(order.UserId);
        var paymentMethodId = order.PaymentMethodId ?? user?.DefaultPaymentMethodId;
        var customerId = order.StripeCustomerId ?? user?.StripeCustomerId;

        if (string.IsNullOrWhiteSpace(paymentMethodId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new ExtensionResult(false,
                "We don't have a card on file for this rental. Add a payment method and try again.");
        }

        if (!_stripe.IsConfigured)
        {
            return new ExtensionResult(false,
                "Payments aren't available right now, so nothing was charged and the dates are unchanged.");
        }

        if (!DateOnly.TryParse(order.PickupDate, out var currentPickup))
        {
            return new ExtensionResult(false, "This rental has no pickup date to move.");
        }

        var newPickup = currentPickup.AddDays(RentalTerms.BaseRentalDays * additionalWeeks);

        var charge = new ExtensionCharge
        {
            ExtensionId = Guid.NewGuid().ToString("N"),
            AdditionalWeeks = additionalWeeks,
            AmountCents = amountCents,
            PreviousPickupDate = order.PickupDate,
            NewPickupDate = newPickup.ToString("yyyy-MM-dd"),
            RequestedByUserId = requestedByUserId
        };

        try
        {
            var intent = await _stripe.ChargeOffSessionAsync(
                customerId,
                paymentMethodId,
                amountCents,
                $"Rental extension for order {order.OrderNumber} ({additionalWeeks} week{(additionalWeeks == 1 ? "" : "s")})",
                new Dictionary<string, string>
                {
                    ["kind"] = "rental_extension",
                    ["orderId"] = order.OrderId,
                    ["orderNumber"] = order.OrderNumber,
                    ["extensionId"] = charge.ExtensionId
                },
                ct);

            charge.StripePaymentIntentId = intent.Id;
            charge.Succeeded = intent.Status == "succeeded";

            if (!charge.Succeeded)
            {
                charge.FailureReason = $"Stripe returned status {intent.Status}.";
            }
        }
        catch (StripeException ex)
        {
            // A declined off-session charge is expected often enough to be a normal outcome, not an error.
            charge.Succeeded = false;
            charge.FailureReason = ex.StripeError?.Message ?? ex.Message;
            charge.StripePaymentIntentId = ex.StripeError?.PaymentIntent?.Id;

            order.Extensions.Add(charge);
            await _orders.SaveAsync(order, ct);

            _logger.LogWarning(ex, "Extension charge declined for order {OrderNumber}", order.OrderNumber);

            return new ExtensionResult(false,
                $"The card was declined: {charge.FailureReason} Update your payment method and try again.",
                charge);
        }

        if (!charge.Succeeded)
        {
            order.Extensions.Add(charge);
            await _orders.SaveAsync(order, ct);
            return new ExtensionResult(false,
                "That payment didn't go through. Nothing was charged and your dates are unchanged.", charge);
        }

        order.Extensions.Add(charge);
        order.RentalWeeks += additionalWeeks;
        order.ExtraWeeksCents += amountCents;
        order.AmountPaidCents += amountCents;
        order.PickupDate = charge.NewPickupDate;

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);
        await _notifier.SendExtensionReceiptAsync(order, charge, ct);

        return new ExtensionResult(true,
            $"Extended by {additionalWeeks} week{(additionalWeeks == 1 ? "" : "s")}. We'll collect on {newPickup:dddd, MMMM d}.",
            charge);
    }
}
