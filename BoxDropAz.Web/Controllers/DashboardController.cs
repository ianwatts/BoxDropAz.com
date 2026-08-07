using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Dashboard;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Models.Payments;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>The signed-in renter's view of their own rentals.</summary>
[Authorize]
[Route("dashboard")]
public sealed class DashboardController : Controller
{
    private readonly IOrderService _orders;
    private readonly IRegionService _regions;
    private readonly IStripeGateway _stripe;
    private readonly RentalExtensionService _extensions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly InventoryService _inventory;
    private readonly IConfiguration _config;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IOrderService orders,
        IRegionService regions,
        IStripeGateway stripe,
        RentalExtensionService extensions,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        InventoryService inventory,
        IConfiguration config,
        ILogger<DashboardController> logger)
    {
        _orders = orders;
        _regions = regions;
        _stripe = stripe;
        _extensions = extensions;
        _userManager = userManager;
        _notifier = notifier;
        _inventory = inventory;
        _config = config;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "My rentals";

        var user = await RequireUserAsync();
        var orders = await _orders.GetForUserAsync(user.Id, ct);

        return View(new CustomerDashboardViewModel
        {
            Active = orders
                .Where(o => o.IsActiveRental || o.Status == OrderStatus.PendingPayment)
                .OrderBy(o => o.DeliveryDate)
                .ToList(),
            Past = orders
                .Where(o => o.Status is OrderStatus.Completed or OrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedAtUtc)
                .ToList(),
            CardBrand = user.CardBrand,
            CardLast4 = user.CardLast4
        });
    }

    [HttpGet("order/{id}")]
    public async Task<IActionResult> Detail(string id, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var order = await LoadOwnedOrderAsync(id, user, ct);
        if (order is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Order {order.OrderNumber}";

        return View(new OrderDetailViewModel
        {
            Order = order,
            Region = await _regions.GetByIdAsync(order.RegionId, ct),
            WeeklyPriceCents = await _extensions.GetWeeklyPriceCentsAsync(order, ct),
            CanExtend = order.IsActiveRental,
            CanCancel = CanCancel(order),
            CardBrand = order.CardBrand ?? user.CardBrand,
            CardLast4 = order.CardLast4 ?? user.CardLast4
        });
    }

    [HttpPost("order/{id}/extend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Extend(string id, int additionalWeeks, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var order = await LoadOwnedOrderAsync(id, user, ct);
        if (order is null)
        {
            return NotFound();
        }

        var result = await _extensions.ExtendAsync(order, additionalWeeks, user.Id, ct);
        TempData[result.Success ? "Success" : "Error"] = result.Message;

        return RedirectToAction(nameof(Detail), new { id });
    }

    [HttpPost("order/{id}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string id, string? reason, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var order = await LoadOwnedOrderAsync(id, user, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (!CanCancel(order))
        {
            TempData["Error"] =
                $"This rental is inside the {RentalTerms.FreeCancellationHours} hour window, so it can't be cancelled online. Give us a call and we'll work it out.";
            return RedirectToAction(nameof(Detail), new { id });
        }

        order.Status = OrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by the customer" : reason;

        order.Notes.Add(new OrderNote
        {
            Body = $"Cancelled by the customer. {order.CancellationReason}",
            AuthorName = user.DisplayName,
            AuthorUserId = user.Id
        });

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);
        await _notifier.SendCancellationAsync(order, order.CancellationReason, ct);
        await _notifier.NotifyStaffCancellationAsync(order, order.CancellationReason, ct);

        TempData["Success"] =
            "Your rental is cancelled. Any refund due will land back on your card within 5 to 10 business days.";

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Displays embedded Stripe Checkout in setup mode to replace the stored card. The webhook
    /// writes the new card back to their account.
    /// </summary>
    [HttpPost("payment-method")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePaymentMethod(CancellationToken ct)
    {
        var user = await RequireUserAsync();

        if (!_stripe.IsConfigured)
        {
            TempData["Error"] = "Card updates aren't available right now. Please give us a call.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var customerId = await _stripe.EnsureCustomerAsync(user, ct);
            var returnUrl = Url.Action(nameof(Index), "Dashboard", null, Request.Scheme)!;

            var session = await _stripe.CreateSetupSessionAsync(
                customerId,
                user.Id,
                returnUrl,
                new Dictionary<string, string>
                {
                    ["kind"] = CheckoutKind.PaymentMethodUpdate,
                    ["userId"] = user.Id
                },
                ct);

            return View("EmbeddedCheckout", new EmbeddedCheckoutViewModel
            {
                Title = "Update payment method",
                Description = "Securely add or replace the card saved to your account.",
                ClientSecret = session.ClientSecret,
                PublishableKey = _config["Stripe:PublishableKey"] ?? string.Empty,
                CancelUrl = returnUrl,
                CancelLabel = "Back to my rentals",
                SummaryTitle = "Card on file",
                SummaryText = "Your card details go directly to Stripe and are never stored on BoxDrop AZ servers."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start a card update for {UserId}", user.Id);
            TempData["Error"] = "We couldn't reach our payment provider. Please try again in a moment.";
            return RedirectToAction(nameof(Index));
        }
    }

    // ---------- helpers ----------

    private static bool CanCancel(RentalOrder order)
    {
        if (order.Status != OrderStatus.Confirmed)
        {
            return false;
        }

        if (!DateOnly.TryParse(order.DeliveryDate, out var delivery))
        {
            return false;
        }

        // Inside the cutoff the route is already planned, so cancellation becomes a phone call.
        var hoursUntilDelivery = delivery.ToDateTime(TimeOnly.MinValue) - DeliveryWindows.TodayInArizona().ToDateTime(TimeOnly.MinValue);
        return hoursUntilDelivery.TotalHours >= RentalTerms.FreeCancellationHours;
    }

    private async Task<RentalOrder?> LoadOwnedOrderAsync(string orderId, ApplicationUser user, CancellationToken ct)
    {
        var order = await _orders.GetAsync(orderId, ct);

        // Ownership is checked here rather than trusting the id in the URL.
        return order is not null && order.UserId == user.Id ? order : null;
    }

    private async Task<ApplicationUser> RequireUserAsync()
        => await _userManager.GetUserAsync(User)
           ?? throw new InvalidOperationException("Signed in but the user record is missing.");
}
