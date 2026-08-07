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
    private readonly InventoryService _inventory;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly ILogger<WorkerController> _logger;

    public WorkerController(
        IOrderService orders,
        IRegionService regions,
        InventoryService inventory,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        ILogger<WorkerController> logger)
    {
        _orders = orders;
        _regions = regions;
        _inventory = inventory;
        _userManager = userManager;
        _notifier = notifier;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? date, string? region, string? view, CancellationToken ct)
    {
        var viewMode = ManifestViewModes.Normalize(view);
        var anchorDate = ParseDate(date);
        var (startDate, endDate) = ResolveRange(anchorDate, viewMode);

        ViewData["Title"] = viewMode switch
        {
            ManifestViewModes.Week => "Weekly manifest",
            ManifestViewModes.Month => "Monthly manifest",
            _ => "Today's manifest"
        };

        var (scopedRegion, canSwitch) = await ResolveRegionAsync(region, ct);

        if (scopedRegion is null)
        {
            return View(new ManifestViewModel
            {
                Date = anchorDate,
                StartDate = startDate,
                EndDate = endDate,
                ViewMode = viewMode,
                AllRegions = await _regions.GetActiveAsync(ct),
                CanSwitchRegion = canSwitch
            });
        }

        var deliveries = await _orders.GetDeliveriesBetweenAsync(scopedRegion.Id, startDate, endDate, ct);
        var pickups = await _orders.GetPickupsBetweenAsync(scopedRegion.Id, startDate, endDate, ct);

        return View(new ManifestViewModel
        {
            Date = anchorDate,
            StartDate = startDate,
            EndDate = endDate,
            ViewMode = viewMode,
            Region = scopedRegion,
            AllRegions = await _regions.GetActiveAsync(ct),
            CanSwitchRegion = canSwitch,
            // An unpaid order isn't a real stop, and a cancelled one must not be driven to.
            Deliveries = deliveries
                .Where(o => o.Status is OrderStatus.Confirmed or OrderStatus.OutForDelivery or OrderStatus.Delivered)
                .OrderBy(o => o.DeliveryDate)
                .ThenBy(o => o.DeliveryWindow)
                .ThenBy(o => o.DeliveryZip)
                .ToList(),
            Pickups = pickups
                .Where(o => o.Status is OrderStatus.Delivered or OrderStatus.OutForPickup or OrderStatus.Completed)
                .OrderBy(o => o.PickupDate)
                .ThenBy(o => o.PickupWindow)
                .ThenBy(o => o.PickupZip)
                .ToList(),
            RestockTasks = await _inventory.GetRestockTasksAsync(scopedRegion.Id, endDate, ct)
        });
    }

    [HttpGet("order/{id}")]
    public async Task<IActionResult> Order(string id, bool pickup, string? date, string? view, CancellationToken ct)
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
            ManifestDate = ParseDate(date),
            ViewMode = ManifestViewModes.Normalize(view)
        });
    }

    [HttpPost("order/{id}/delivered")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkDelivered(string id, string? date, string? view, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.DeliveredAtUtc is not null)
        {
            TempData["Info"] = $"{order.OrderNumber} was already marked delivered.";
            return RedirectToManifest(date, view);
        }

        var user = await RequireUserAsync();

        if (order.RequiresIndexCard && order.IndexCardIssuedAtUtc is null)
        {
            if (!await _inventory.ConsumeIndexCardAsync(order.RegionId, ct))
            {
                TempData["Error"] =
                    $"Need at least {InventoryService.IndexCardsPerPack} colored index cards (1 pack) in inventory before delivery.";
                return RedirectToManifest(date, view);
            }

            order.IndexCardIssuedAtUtc = DateTime.UtcNow;
        }

        var previous = order.Status;
        order.Status = OrderStatus.Delivered;
        order.DeliveredAtUtc = DateTime.UtcNow;
        order.Notes.Add(new OrderNote
        {
            Body = $"Delivered {order.CrateCount} totes with lids, {order.DollyCount} custom-fit dollies" +
                   (order.RequiresIndexCard
                       ? $", and 1 package of {InventoryService.IndexCardsPerPack} color-coded 3x5 cards."
                       : "."),
            AuthorName = user.DisplayName,
            AuthorUserId = user.Id
        });

        await _orders.SaveAsync(order, ct);
        await _notifier.NotifyStaffStatusChangedAsync(order, previous, OrderStatus.Delivered, user.DisplayName, ct);

        TempData["Success"] = $"{order.OrderNumber} marked delivered.";
        return RedirectToManifest(date, view);
    }

    [HttpPost("order/{id}/picked-up")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkPickedUp(
        string id,
        int cratesReturned,
        int dolliesReturned,
        string? date,
        string? view,
        CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.PickedUpAtUtc is not null)
        {
            TempData["Info"] = $"{order.OrderNumber} was already marked collected.";
            return RedirectToManifest(date, view);
        }

        var user = await RequireUserAsync();
        var returned = Math.Clamp(cratesReturned, 0, order.CrateCount);
        var returnedDollies = Math.Clamp(dolliesReturned, 0, order.DollyCount);
        var previous = order.Status;

        order.Status = OrderStatus.Completed;
        order.PickedUpAtUtc = DateTime.UtcNow;
        order.CratesReturned = returned;
        order.DolliesReturned = returnedDollies;
        order.Notes.Add(new OrderNote
        {
            Body = $"Collected. {returned} of {order.CrateCount} totes with lids and " +
                   $"{returnedDollies} of {order.DollyCount} custom-fit dollies returned.",
            AuthorName = user.DisplayName,
            AuthorUserId = user.Id
        });

        // Short count is the most common charge, so raise it here rather than making the driver
        // remember to file it separately.
        var missing = order.CrateCount - returned;
        var missingDollies = order.DollyCount - returnedDollies;
        var newDamages = new List<DamageLine>();
        if (missing > 0)
        {
            var region = await _regions.GetByIdAsync(order.RegionId, ct);
            var unit = DamageKinds.UnitAmountCents(
                DamageKinds.Crate, order.Terms, region?.DamageFees ?? new DamageFeeSchedule());

            var line = new DamageLine
            {
                Kind = DamageKinds.Crate,
                Quantity = missing,
                UnitAmountCents = unit,
                Description = $"{missing} tote{(missing == 1 ? "" : "s")} with lid not returned at pickup",
                Status = DamageChargeStatus.PendingReview,
                ReportedByUserId = user.Id,
                ReportedByName = user.DisplayName
            };
            order.Damages.Add(line);
            newDamages.Add(line);
        }

        if (missingDollies > 0)
        {
            var region = await _regions.GetByIdAsync(order.RegionId, ct);
            var unit = DamageKinds.UnitAmountCents(
                DamageKinds.Dolly, order.Terms, region?.DamageFees ?? new DamageFeeSchedule());

            var line = new DamageLine
            {
                Kind = DamageKinds.Dolly,
                Quantity = missingDollies,
                UnitAmountCents = unit,
                Description = $"{missingDollies} custom-fit dolly/dollies not returned at pickup",
                Status = DamageChargeStatus.PendingReview,
                ReportedByUserId = user.Id,
                ReportedByName = user.DisplayName
            };
            order.Damages.Add(line);
            newDamages.Add(line);
        }

        await _orders.SaveAsync(order, ct);
        await _inventory.RecordMissingAssetsAsync(order.RegionId, missing, missingDollies, ct);
        await _notifier.NotifyStaffStatusChangedAsync(order, previous, OrderStatus.Completed, user.DisplayName, ct);

        foreach (var damage in newDamages)
        {
            await _notifier.NotifyStaffDamagePendingAsync(order, damage, ct);
        }

        TempData["Success"] = missing > 0 || missingDollies > 0
            ? $"{order.OrderNumber} collected. Missing assets were queued for admin review and inventory replenishment."
            : $"{order.OrderNumber} collected, all totes, lids, and dollies accounted for.";

        return RedirectToManifest(date, view);
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
        await _notifier.NotifyStaffDamagePendingAsync(order, order.Damages[^1], ct);

        _logger.LogInformation(
            "{Worker} reported {Quantity} x {Kind} on order {OrderNumber}",
            user.DisplayName, count, damageKind.Code, order.OrderNumber);

        TempData["Success"] =
            $"Reported {count} \u00d7 {damageKind.Label} ({Money.Format(count * unit)}). An admin reviews it before anything is charged.";

        return RedirectToAction(nameof(Order), new { id, pickup = true });
    }

    [HttpPost("inventory/{taskId}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteRestock(
        string taskId,
        string regionId,
        int totesReceived,
        int dolliesReceived,
        int cardHolderPacksReceived,
        int cardPacksReceived,
        string? date,
        string? view,
        CancellationToken ct)
    {
        var (region, _) = await ResolveRegionAsync(regionId, ct);
        if (region is null || region.Id != regionId)
        {
            return NotFound();
        }

        var user = await RequireUserAsync();
        var completed = await _inventory.CompleteRestockTaskAsync(
            regionId,
            taskId,
            totesReceived,
            dolliesReceived,
            cardHolderPacksReceived,
            cardPacksReceived,
            user.Id,
            user.DisplayName,
            ct);

        TempData[completed ? "Success" : "Info"] = completed
            ? $"Inventory received: {Math.Max(0, totesReceived)} totes, {Math.Max(0, dolliesReceived)} dollies, " +
              $"{Math.Max(0, cardHolderPacksReceived)} holder pack(s), and {Math.Max(0, cardPacksReceived)} card pack(s). " +
              "Only totes equipped with holders were added to usable stock."
            : "That inventory task was already completed or cancelled.";
        return RedirectToManifest(date, view, regionId);
    }

    // ---------- helpers ----------

    private IActionResult RedirectToManifest(string? date, string? view, string? region = null)
        => RedirectToAction(nameof(Index), new
        {
            date,
            view = ManifestViewModes.Normalize(view),
            region
        });

    private static DateOnly ParseDate(string? value)
        => DateOnly.TryParse(value, out var parsed) ? parsed : DeliveryWindows.TodayInArizona();

    private static (DateOnly Start, DateOnly End) ResolveRange(DateOnly anchor, string viewMode)
    {
        return viewMode switch
        {
            ManifestViewModes.Week =>
            (
                StartOfWeek(anchor),
                StartOfWeek(anchor).AddDays(6)
            ),
            ManifestViewModes.Month =>
            (
                new DateOnly(anchor.Year, anchor.Month, 1),
                new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(1).AddDays(-1)
            ),
            _ => (anchor, anchor)
        };
    }

    /// <summary>Monday-start week, matching typical operations planning.</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-offset);
    }

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

        if (User.IsInRole(Roles.SaaSAdmin) || User.IsInRole(Roles.RegionalAdmin))
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
