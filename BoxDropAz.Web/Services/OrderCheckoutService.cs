using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Turns a completed Stripe Checkout session into a confirmed rental. The browser return and the
/// webhook both call this, in whichever order they arrive, so it is written to be safely repeatable.
/// </summary>
public sealed class OrderCheckoutService
{
    private readonly IOrderService _orders;
    private readonly IGiftService _gifts;
    private readonly IStripeGateway _stripe;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly GiftNotifier _giftNotifier;
    private readonly InventoryService _inventory;
    private readonly SiteUrls _urls;
    private readonly ILogger<OrderCheckoutService> _logger;

    public OrderCheckoutService(
        IOrderService orders,
        IGiftService gifts,
        IStripeGateway stripe,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        GiftNotifier giftNotifier,
        InventoryService inventory,
        SiteUrls urls,
        ILogger<OrderCheckoutService> logger)
    {
        _orders = orders;
        _gifts = gifts;
        _stripe = stripe;
        _userManager = userManager;
        _notifier = notifier;
        _giftNotifier = giftNotifier;
        _inventory = inventory;
        _urls = urls;
        _logger = logger;
    }

    /// <summary>
    /// Confirms the order attached to a session. Returns true only for the call that actually moved
    /// the order out of PendingPayment, so the confirmation email is sent exactly once.
    /// </summary>
    public async Task<bool> ConfirmFromSessionAsync(RentalOrder order, Stripe.Checkout.Session session, CancellationToken ct = default)
    {
        if (order.Status != OrderStatus.PendingPayment)
        {
            return false;
        }

        var (paymentMethodId, brand, last4) = await _stripe.GetSessionPaymentMethodAsync(session, ct);

        order.Status = OrderStatus.Confirmed;
        order.StripePaymentIntentId = session.PaymentIntentId;
        order.PaymentMethodId = paymentMethodId;
        order.CardBrand = brand;
        order.CardLast4 = last4;
        // A setup-mode session has no amount; the gift covered the whole thing.
        order.AmountPaidCents = (int)(session.AmountTotal ?? 0);
        order.ConfirmedAtUtc = DateTime.UtcNow;

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);

        await StoreCardOnUserAsync(order.UserId, paymentMethodId, brand, last4, ct);

        if (!string.IsNullOrWhiteSpace(order.GiftId))
        {
            await MarkGiftClaimedAsync(order, ct);
        }

        await _notifier.SendOrderConfirmationAsync(order, _urls.OrderDetail(order.OrderId), ct);

        _logger.LogInformation("Confirmed order {OrderNumber} from session {SessionId}", order.OrderNumber, session.Id);
        return true;
    }

    /// <summary>
    /// Keeps the card on the user record too, so extensions and damage charges on any of their
    /// orders have something to bill without another checkout.
    /// </summary>
    public async Task StoreCardOnUserAsync(string userId, string? paymentMethodId, string? brand, string? last4, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paymentMethodId) || string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return;
        }

        user.DefaultPaymentMethodId = paymentMethodId;
        user.CardBrand = brand;
        user.CardLast4 = last4;
        await _userManager.UpdateAsync(user);
    }

    private async Task MarkGiftClaimedAsync(RentalOrder order, CancellationToken ct)
    {
        var gift = await _gifts.GetAsync(order.GiftId!, ct);
        if (gift is null || gift.Status != GiftStatus.Sent)
        {
            return;
        }

        gift.Status = GiftStatus.Claimed;
        gift.ClaimedAtUtc = DateTime.UtcNow;
        gift.RentalOrderId = order.OrderId;
        await _gifts.SaveAsync(gift, ct);

        var deliveryDate = DateOnly.TryParse(order.DeliveryDate, out var parsed)
            ? parsed.ToString("dddd, MMMM d")
            : order.DeliveryDate;

        await _giftNotifier.SendClaimedNoticeAsync(gift, deliveryDate, ct);
    }
}
