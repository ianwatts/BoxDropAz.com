using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;
using Stripe;

namespace BoxDropAz.Web.Services;

public sealed record DamageChargeResult(bool Success, string Message, int ChargedCents);

/// <summary>
/// Settles worker-reported damage against the card on file, once an admin has approved it. Kept out
/// of the controller because the money movement and the audit trail have to stay together.
/// </summary>
public sealed class DamageChargeService
{
    private readonly IOrderService _orders;
    private readonly IStripeGateway _stripe;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly ILogger<DamageChargeService> _logger;

    public DamageChargeService(
        IOrderService orders,
        IStripeGateway stripe,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        ILogger<DamageChargeService> logger)
    {
        _orders = orders;
        _stripe = stripe;
        _userManager = userManager;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>
    /// Charges every pending damage line in one PaymentIntent, so the renter sees a single amount on
    /// their statement rather than a scatter of small charges.
    /// </summary>
    public async Task<DamageChargeResult> ApproveAndChargeAsync(
        RentalOrder order,
        IReadOnlyCollection<string> damageIds,
        ApplicationUser approvedBy,
        CancellationToken ct = default)
    {
        var lines = order.Damages
            .Where(d => d.Status == DamageChargeStatus.PendingReview && damageIds.Contains(d.DamageId))
            .ToList();

        if (lines.Count == 0)
        {
            return new DamageChargeResult(false, "Nothing was selected to charge.", 0);
        }

        var totalCents = lines.Sum(d => d.TotalCents);
        if (totalCents <= 0)
        {
            return new DamageChargeResult(false, "Those lines add up to nothing, so there's no charge to make.", 0);
        }

        var user = await _userManager.FindByIdAsync(order.UserId);
        var paymentMethodId = order.PaymentMethodId ?? user?.DefaultPaymentMethodId;
        var customerId = order.StripeCustomerId ?? user?.StripeCustomerId;

        if (string.IsNullOrWhiteSpace(paymentMethodId) || string.IsNullOrWhiteSpace(customerId))
        {
            return new DamageChargeResult(false,
                "There's no card on file for this rental, so this has to be invoiced manually.", 0);
        }

        // Leave the lines pending rather than marking them failed: the charge was never attempted,
        // so an admin should be able to retry once payments are back.
        if (!_stripe.IsConfigured)
        {
            return new DamageChargeResult(false,
                "Payments aren't available right now. These lines are still pending, so try again shortly.", 0);
        }

        var description = $"Equipment charges for order {order.OrderNumber}";

        PaymentIntent? intent = null;
        string? failure = null;

        try
        {
            intent = await _stripe.ChargeOffSessionAsync(
                customerId,
                paymentMethodId,
                totalCents,
                description,
                new Dictionary<string, string>
                {
                    ["kind"] = "damage_charge",
                    ["orderId"] = order.OrderId,
                    ["orderNumber"] = order.OrderNumber,
                    ["damageIds"] = string.Join(",", lines.Select(l => l.DamageId))
                },
                ct);

            if (intent.Status != "succeeded")
            {
                failure = $"Stripe returned status {intent.Status}.";
            }
        }
        catch (StripeException ex)
        {
            failure = ex.StripeError?.Message ?? ex.Message;
            _logger.LogWarning(ex, "Damage charge declined for order {OrderNumber}", order.OrderNumber);
        }

        var succeeded = failure is null;

        foreach (var line in lines)
        {
            line.Status = succeeded ? DamageChargeStatus.Charged : DamageChargeStatus.ChargeFailed;
            line.ResolvedByUserId = approvedBy.Id;
            line.ResolvedAtUtc = DateTime.UtcNow;
            line.StripePaymentIntentId = intent?.Id;
            line.FailureReason = failure;
        }

        order.Notes.Add(new OrderNote
        {
            Body = succeeded
                ? $"Charged {Money.Format(totalCents)} in equipment fees ({lines.Count} line{(lines.Count == 1 ? "" : "s")})."
                : $"Equipment charge of {Money.Format(totalCents)} failed: {failure}",
            AuthorName = approvedBy.DisplayName,
            AuthorUserId = approvedBy.Id
        });

        if (succeeded)
        {
            order.AmountPaidCents += totalCents;
        }

        await _orders.SaveAsync(order, ct);

        if (succeeded)
        {
            await _notifier.SendDamageChargeAsync(order, lines, totalCents, ct);
            return new DamageChargeResult(true,
                $"Charged {Money.Format(totalCents)} and emailed the renter a breakdown.", totalCents);
        }

        await _notifier.NotifyStaffDamageChargeFailedAsync(order, totalCents, failure, ct);

        return new DamageChargeResult(false,
            $"The card was declined: {failure} The lines are marked failed so you can follow up.", 0);
    }

    public async Task<DamageChargeResult> WaiveAsync(
        RentalOrder order,
        IReadOnlyCollection<string> damageIds,
        ApplicationUser waivedBy,
        string? reason,
        CancellationToken ct = default)
    {
        var lines = order.Damages
            .Where(d => d.Status == DamageChargeStatus.PendingReview && damageIds.Contains(d.DamageId))
            .ToList();

        if (lines.Count == 0)
        {
            return new DamageChargeResult(false, "Nothing was selected to waive.", 0);
        }

        var totalCents = lines.Sum(d => d.TotalCents);

        foreach (var line in lines)
        {
            line.Status = DamageChargeStatus.Waived;
            line.ResolvedByUserId = waivedBy.Id;
            line.ResolvedAtUtc = DateTime.UtcNow;
        }

        order.Notes.Add(new OrderNote
        {
            Body = string.IsNullOrWhiteSpace(reason)
                ? $"Waived {Money.Format(totalCents)} in equipment fees."
                : $"Waived {Money.Format(totalCents)} in equipment fees: {reason}",
            AuthorName = waivedBy.DisplayName,
            AuthorUserId = waivedBy.Id
        });

        await _orders.SaveAsync(order, ct);

        return new DamageChargeResult(true, $"Waived {Money.Format(totalCents)}. The renter isn't charged.", 0);
    }
}
