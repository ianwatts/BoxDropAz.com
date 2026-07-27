using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Models.Worker;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// The driver's day: what to drop off, what to collect, and what came back broken. Deliberately
/// thin so it works one handed on a phone in a driveway.
/// </summary>
[Authorize(Policy = "Fulfillment")]
[Route("worker")]
public sealed class WorkerController : Controller
{
    private readonly IOrderService _orders;
    private readonly IRegionService _regions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<WorkerController> _logger;

    public WorkerController(
        IOrderService orders,
        IRegionService regions,
        UserManager<ApplicationUser> userManager,
        ILogger<WorkerController> logger)
    {
        _orders = orders;
        _regions = regions;
        _userManager = userManager;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? date, string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Today's manifest";

        var manifestDate = ParseDate(date);
        var (scopedRegion, canSwitch) = await ResolveRegionAsync(region, ct);

        if (scopedRegion is null)
        {
            return View(new ManifestViewModel
            {
                Date = manifestDate,
                AllRegions = await _regions.GetActiveAsync(ct),
                CanSwitchRegion = canSwitch
            });
        }

        var deliveries = await _orders.GetDeliveriesAsync(scopedRegion.Id, manifestDate, ct);
        var pickups = await _orders.GetPickupsAsync(scopedRegion.Id, manifestDate, ct);

        return View(new ManifestViewModel
        {
            Date = manifestDate,
            Region = scopedRegion,
            AllRegions = await _regions.GetActiveAsync(ct),
            CanSwitchRegion = canSwitch,
            // An unpaid order isn't a real stop, and a cancelled one must not be driven to.
            Deliveries = deliveries
                .Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.OutForDelivery or OrderStatus.Delivered)
                .OrderBy(o => o.DeliveryWindow)
                .ThenBy(o => o.DeliveryZip)
                .ToList(),
            Pickups = pickups
                .Where(o => o.Status is OrderStatus.Delivered or OrderStatus.OutForPickup or OrderStatus.Completed)
                .OrderBy(o => o.PickupWindow)
                .ThenBy(o => o.PickupZip)
                .ToList()
        });
    }

    [HttpGet("order/{id}")]
    public async Task<IActionResult> Order(string id, bool pickup, string? date, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"{(pickup ? "Pickup" : "Delivery")} {order.OrderNumber}";

        return View(new WorkerOrderViewModel
        {
            Order = order,
            Region = await _regions.GetByIdAsync(order.RegionId, ct),
            IsPickup = pickup,
            ManifestDate = ParseDate(date)
        });
    }

    [HttpPost("order/{id}/delivered")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDelivered(string id, string? date, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.DeliveredAtUtc is not null)
        {
            TempData["Info"] = $"{order.OrderNumber} was already marked delivered.";
            return RedirectToAction(nameof(Index), new { date });
        }

        var user = await RequireUserAsync();

        order.Status = OrderStatus.Delivered;
        order.DeliveredAtUtc = DateTime.UtcNow;
        order.Notes.Add(new OrderNote
        {
            Body = $"Delivered {order.CrateCount} crates and {order.DollyCount} dollies.",
            AuthorName = user.DisplayName,
            AuthorUserId = user.Id
        });

        await _orders.SaveAsync(order, ct);

        TempData["Success"] = $"{order.OrderNumber} marked delivered.";
        return RedirectToAction(nameof(Index), new { date });
    }

    [HttpPost("order/{id}/picked-up")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPickedUp(string id, int cratesReturned, string? date, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.PickedUpAtUtc is not null)
        {
            TempData["Info"] = $"{order.OrderNumber} was already marked collected.";
            return RedirectToAction(nameof(Index), new { date });
        }

        var user = await RequireUserAsync();
        var returned = Math.Clamp(cratesReturned, 0, order.CrateCount);

        order.Status = OrderStatus.Completed;
        order.PickedUpAtUtc = DateTime.UtcNow;
        order.CratesReturned = returned;
        order.Notes.Add(new OrderNote
        {
            Body = $"Collected. {returned} of {order.CrateCount} crates returned.",
            AuthorName = user.DisplayName,
            AuthorUserId = user.Id
        });

        // Short count is the most common charge, so raise it here rather than making the driver
        // remember to file it separately.
        var missing = order.CrateCount - returned;
        if (missing > 0)
        {
            var region = await _regions.GetByIdAsync(order.RegionId, ct);
            var unit = DamageKinds.UnitAmountCents(
                DamageKinds.Crate, order.Terms, region?.DamageFees ?? new DamageFeeSchedule());

            order.Damages.Add(new DamageLine
            {
                Kind = DamageKinds.Crate,
                Quantity = missing,
                UnitAmountCents = unit,
                Description = $"{missing} crate{(missing == 1 ? "" : "s")} not returned at pickup",
                Status = DamageChargeStatus.PendingReview,
                ReportedByUserId = user.Id,
                ReportedByName = user.DisplayName
            });
        }

        await _orders.SaveAsync(order, ct);

        TempData["Success"] = missing > 0
            ? $"{order.OrderNumber} collected. {missing} missing crate{(missing == 1 ? "" : "s")} queued for admin review."
            : $"{order.OrderNumber} collected, all crates accounted for.";

        return RedirectToAction(nameof(Index), new { date });
    }

    /// <summary>
    /// Queues a charge rather than taking the money. Only an admin can put it on the card, which
    /// keeps a disputed judgement call off the driver.
    /// </summary>
    [HttpPost("order/{id}/report")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ReportDamage(
        string id,
        string kind,
        int quantity,
        string? description,
        CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        var damageKind = DamageKinds.FromCode(kind);
        if (damageKind is null)
        {
            TempData["Error"] = "Pick what needs reporting.";
            return RedirectToAction(nameof(Order), new { id, pickup = true });
        }

        var user = await RequireUserAsync();
        var region = await _regions.GetByIdAsync(order.RegionId, ct);
        var unit = DamageKinds.UnitAmountCents(
            damageKind.Code, order.Terms, region?.DamageFees ?? new DamageFeeSchedule());

        var count = damageKind.PerUnit ? Math.Max(1, quantity) : 1;

        order.Damages.Add(new DamageLine
        {
            Kind = damageKind.Code,
            Quantity = count,
            UnitAmountCents = unit,
            Description = description?.Trim() ?? string.Empty,
            Status = DamageChargeStatus.PendingReview,
            ReportedByUserId = user.Id,
            ReportedByName = user.DisplayName
        });

        await _orders.SaveAsync(order, ct);

        _logger.LogInformation(
            "{Worker} reported {Quantity} x {Kind} on order {OrderNumber}",
            user.DisplayName, count, damageKind.Code, order.OrderNumber);

        TempData["Success"] =
            $"Reported {count} \u00d7 {damageKind.Label} ({Money.Format(count * unit)}). An admin reviews it before anything is charged.";

        return RedirectToAction(nameof(Order), new { id, pickup = true });
    }

    // ---------- helpers ----------

    private static DateOnly ParseDate(string? value)
        => DateOnly.TryParse(value, out var parsed) ? parsed : DeliveryWindows.TodayInArizona();

    /// <summary>
    /// Workers see only their own region; admins can page through any of them.
    /// </summary>
    private async Task<(Region? Region, bool CanSwitch)> ResolveRegionAsync(string? requested, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var canSwitch = User.IsInRole(Roles.SaaSAdmin) || User.IsInRole(Roles.RegionalAdmin);

        if (canSwitch && !string.IsNullOrWhiteSpace(requested))
        {
            return (await _regions.GetByIdAsync(requested, ct) ?? await _regions.GetDefaultAsync(ct), true);
        }

        var own = string.IsNullOrWhiteSpace(user.RegionId)
            ? await _regions.GetDefaultAsync(ct)
            : await _regions.GetByIdAsync(user.RegionId, ct) ?? await _regions.GetDefaultAsync(ct);

        return (own, canSwitch);
    }

    /// <summary>
    /// Loads an order only if the caller is allowed to touch it. A worker is confined to their own
    /// region so a guessed order id from another market goes nowhere.
    /// </summary>
    private async Task<RentalOrder?> LoadInScopeAsync(string orderId, CancellationToken ct)
    {
        var order = await _orders.GetAsync(orderId, ct);
        if (order is null)
        {
            return null;
        }

        if (User.IsInRole(Roles.SaaSAdmin))
        {
            return order;
        }

        var user = await RequireUserAsync();
        return string.IsNullOrWhiteSpace(user.RegionId) || order.RegionId == user.RegionId ? order : null;
    }

    private async Task<ApplicationUser> RequireUserAsync()
        => await _userManager.GetUserAsync(User)
           ?? throw new InvalidOperationException("Signed in but the user record is missing.");
}
