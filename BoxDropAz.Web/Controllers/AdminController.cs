using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;
using BoxDropAz.Web.Models.Admin;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// Regional operations: how the market is performing, and the levers to fix a specific rental.
/// Everything here is scoped to one region; a platform admin can page across them.
/// </summary>
[Authorize(Policy = "AnyAdmin")]
[Route("admin")]
public sealed class AdminController : Controller
{
    private readonly IOrderService _orders;
    private readonly IRegionService _regions;
    private readonly IGiftService _gifts;
    private readonly RentalExtensionService _extensions;
    private readonly DamageChargeService _damages;
    private readonly InventoryService _inventory;
    private readonly DynamoDbDataHelper _data;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IOrderService orders,
        IRegionService regions,
        IGiftService gifts,
        RentalExtensionService extensions,
        DamageChargeService damages,
        InventoryService inventory,
        DynamoDbDataHelper data,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        ILogger<AdminController> logger)
    {
        _orders = orders;
        _regions = regions;
        _gifts = gifts;
        _extensions = extensions;
        _damages = damages;
        _inventory = inventory;
        _data = data;
        _userManager = userManager;
        _notifier = notifier;
        _logger = logger;
    }

    // ---------- dashboard ----------

    [HttpGet("")]
    public async Task<IActionResult> Index(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Regional dashboard";

        var (scoped, canSwitch) = await ResolveRegionAsync(region, ct);
        var model = new AdminDashboardViewModel
        {
            Region = scoped,
            AllRegions = await _regions.GetAllAsync(ct),
            CanSwitchRegion = canSwitch
        };

        if (scoped is null)
        {
            return View(model);
        }

        var today = DeliveryWindows.TodayInArizona();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var windowStart = monthStart.AddMonths(-12);

        var orders = await _orders.GetCreatedBetweenAsync(
            scoped.Id,
            windowStart.ToDateTime(TimeOnly.MinValue),
            today.AddDays(1).ToDateTime(TimeOnly.MinValue),
            ct);

        // Cancelled orders never became revenue, so they are excluded from every money figure.
        var billable = orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.PendingPayment).ToList();

        var thisMonth = billable.Where(o => o.CreatedAtUtc >= monthStart.ToDateTime(TimeOnly.MinValue)).ToList();
        var lastMonthStart = monthStart.AddMonths(-1);
        var lastMonth = billable
            .Where(o => o.CreatedAtUtc >= lastMonthStart.ToDateTime(TimeOnly.MinValue)
                        && o.CreatedAtUtc < monthStart.ToDateTime(TimeOnly.MinValue))
            .ToList();

        var pendingDamages = billable.SelectMany(o => o.Damages)
            .Where(d => d.Status == DamageChargeStatus.PendingReview)
            .ToList();

        var active = billable.Where(o => o.IsActiveRental).ToList();

        model.RevenueThisMonthCents = thisMonth.Sum(o => o.AmountPaidCents);
        model.RevenueLastMonthCents = lastMonth.Sum(o => o.AmountPaidCents);
        model.OrdersThisMonth = thisMonth.Count;
        model.ActiveRentals = active.Count;
        model.CratesInTheField = active
            .Where(o => o.DeliveredAtUtc is not null && o.PickedUpAtUtc is null)
            .Sum(o => o.CrateCount);
        model.PendingDamageCents = pendingDamages.Sum(d => d.TotalCents);
        model.PendingDamageCount = pendingDamages.Count;
        model.GiftOrdersThisMonth = thisMonth.Count(o => o.Source == OrderSource.RealtorGift);
        model.DailyRevenue = BuildDailySeries(billable, today, 30);
        model.MonthlyRevenue = BuildMonthlySeries(billable, monthStart, 12);

        model.UpcomingDeliveries = billable
            .Where(o => o.DeliveredAtUtc is null && string.Compare(o.DeliveryDate, today.ToString("yyyy-MM-dd"), StringComparison.Ordinal) >= 0)
            .OrderBy(o => o.DeliveryDate)
            .Take(8)
            .ToList();

        model.NeedsAttention = billable
            .Where(o => o.Damages.Any(d => d.Status is DamageChargeStatus.PendingReview or DamageChargeStatus.ChargeFailed)
                        || o.Extensions.Any(e => !e.Succeeded)
                        || IsOverduePickup(o, today))
            .OrderBy(o => o.PickupDate)
            .Take(10)
            .ToList();

        return View(model);
    }

    // ---------- inventory ----------

    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Inventory";
        var (scoped, canSwitch) = await ResolveRegionAsync(region, ct);
        var model = new InventoryViewModel
        {
            Region = scoped,
            AllRegions = await _regions.GetAllAsync(ct),
            CanSwitchRegion = canSwitch
        };

        if (scoped is not null)
        {
            model.Assessment = await _inventory.GetAssessmentAsync(scoped.Id, reconcileTasks: true, ct);
            model.OpenTasks = model.Assessment.OpenRestockTasks;
        }

        return View(model);
    }

    [HttpPost("inventory")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInventory(
        string region,
        InventoryUpdateModel form,
        CancellationToken ct)
    {
        var (scoped, _) = await ResolveRegionAsync(region, ct);
        if (scoped is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Inventory totals must be zero or greater.";
            return RedirectToAction(nameof(Inventory), new { region = scoped.Id });
        }

        await _inventory.SetTotalsAsync(
            scoped.Id,
            form.TotalTotes,
            form.TotalDollies,
            form.TotalIndexCards,
            form.TotalCardHolders,
            ct);
        TempData["Success"] = "Inventory totals updated and future shortages recalculated.";
        return RedirectToAction(nameof(Inventory), new { region = scoped.Id });
    }

    // ---------- scheduling ----------

    [HttpGet("schedule")]
    public async Task<IActionResult> Schedule(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Delivery and pickup schedule";
        var (scoped, canSwitch) = await ResolveRegionAsync(region, ct);
        return View(new ScheduleSettingsViewModel
        {
            Region = scoped,
            AllRegions = await _regions.GetAllAsync(ct),
            CanSwitchRegion = canSwitch,
            Settings = scoped?.Scheduling ?? new SchedulingSettings()
        });
    }

    [HttpPost("schedule/settings")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateScheduleSettings(
        string region,
        ScheduleSettingsUpdateModel form,
        CancellationToken ct)
    {
        var (scoped, _) = await ResolveRegionAsync(region, ct);
        if (scoped is null)
        {
            return NotFound();
        }

        var groups = new[]
        {
            form.WeekdayDeliveryWindows,
            form.WeekdayPickupWindows,
            form.WeekendDeliveryWindows,
            form.WeekendPickupWindows
        };
        if (!ModelState.IsValid || groups.Any(group => NormalizeWindows(group).Count == 0))
        {
            TempData["Error"] = "Select at least one valid window for each delivery and pickup group.";
            return RedirectToAction(nameof(Schedule), new { region = scoped.Id });
        }

        var settings = scoped.Scheduling ?? new SchedulingSettings();
        settings.MinimumNoticeDays = Math.Clamp(form.MinimumNoticeDays, 0, 30);
        settings.WeekdayDeliveryWindows = NormalizeWindows(form.WeekdayDeliveryWindows);
        settings.WeekdayPickupWindows = NormalizeWindows(form.WeekdayPickupWindows);
        settings.WeekendDeliveryWindows = NormalizeWindows(form.WeekendDeliveryWindows);
        settings.WeekendPickupWindows = NormalizeWindows(form.WeekendPickupWindows);
        scoped.Scheduling = settings;
        await _regions.SaveAsync(scoped, ct);

        TempData["Success"] = "Delivery and pickup availability updated.";
        return RedirectToAction(nameof(Schedule), new { region = scoped.Id });
    }

    [HttpPost("schedule/blackouts")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddScheduleBlackout(
        string region,
        ScheduleBlackoutModel form,
        CancellationToken ct)
    {
        var (scoped, _) = await ResolveRegionAsync(region, ct);
        if (scoped is null)
        {
            return NotFound();
        }

        if (!DateOnly.TryParse(form.Date, out var date)
            || date < DeliveryWindows.TodayInArizona()
            || !ScheduleOperations.IsValid(form.Operation)
            || (form.Window != DeliveryWindows.AllDay && !DeliveryWindows.IsValid(form.Window)))
        {
            TempData["Error"] = "Choose a future date, operation, and valid time window.";
            return RedirectToAction(nameof(Schedule), new { region = scoped.Id });
        }

        var settings = scoped.Scheduling ?? new SchedulingSettings();
        var duplicate = settings.Blackouts.Any(b =>
            b.Date == date.ToString("yyyy-MM-dd")
            && b.Operation == form.Operation
            && b.Window == form.Window);
        if (!duplicate)
        {
            settings.Blackouts.Add(new ScheduleBlackout
            {
                Date = date.ToString("yyyy-MM-dd"),
                Operation = form.Operation,
                Window = form.Window,
                Reason = form.Reason?.Trim()
            });
            scoped.Scheduling = settings;
            await _regions.SaveAsync(scoped, ct);
        }

        TempData[duplicate ? "Info" : "Success"] = duplicate
            ? "That unavailable slot is already listed."
            : "Unavailable slot added.";
        return RedirectToAction(nameof(Schedule), new { region = scoped.Id });
    }

    [HttpPost("schedule/blackouts/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteScheduleBlackout(
        string id,
        string region,
        CancellationToken ct)
    {
        var (scoped, _) = await ResolveRegionAsync(region, ct);
        if (scoped is null)
        {
            return NotFound();
        }

        var settings = scoped.Scheduling ?? new SchedulingSettings();
        settings.Blackouts.RemoveAll(b => b.Id == id);
        scoped.Scheduling = settings;
        await _regions.SaveAsync(scoped, ct);
        TempData["Success"] = "Unavailable slot removed.";
        return RedirectToAction(nameof(Schedule), new { region = scoped.Id });
    }

    // ---------- orders ----------

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(
        string? region,
        string? status,
        string? search,
        string? from,
        string? to,
        CancellationToken ct)
    {
        ViewData["Title"] = "Orders";

        var (scoped, canSwitch) = await ResolveRegionAsync(region, ct);
        var model = new AdminOrderListViewModel
        {
            Region = scoped,
            AllRegions = await _regions.GetAllAsync(ct),
            CanSwitchRegion = canSwitch,
            StatusFilter = status,
            Search = search,
            FromDate = from,
            ToDate = to
        };

        if (scoped is null)
        {
            return View(model);
        }

        var fromDate = DateOnly.TryParse(from, out var parsedFrom)
            ? parsedFrom
            : DeliveryWindows.TodayInArizona().AddDays(-90);
        var toDate = DateOnly.TryParse(to, out var parsedTo) ? parsedTo : DeliveryWindows.TodayInArizona();

        var orders = await _orders.GetCreatedBetweenAsync(
            scoped.Id,
            fromDate.ToDateTime(TimeOnly.MinValue),
            toDate.AddDays(1).ToDateTime(TimeOnly.MinValue),
            ct);

        if (Enum.TryParse<OrderStatus>(status, true, out var statusFilter))
        {
            orders = orders.Where(o => o.Status == statusFilter).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            orders = orders.Where(o =>
                    o.OrderNumber.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || o.CustomerName.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || o.CustomerEmail.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || o.CustomerPhone.Contains(term, StringComparison.OrdinalIgnoreCase)
                    || o.DeliveryZip.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        model.Orders = orders.OrderByDescending(o => o.CreatedAtUtc).ToList();
        model.FromDate = fromDate.ToString("yyyy-MM-dd");
        model.ToDate = toDate.ToString("yyyy-MM-dd");

        return View(model);
    }

    [HttpGet("orders/{id}")]
    public async Task<IActionResult> Order(string id, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Order {order.OrderNumber}";

        return View(new AdminOrderDetailViewModel
        {
            Order = order,
            Region = await _regions.GetByIdAsync(order.RegionId, ct),
            Customer = await _userManager.FindByIdAsync(order.UserId),
            WeeklyPriceCents = await _extensions.GetWeeklyPriceCentsAsync(order, ct),
            Gift = string.IsNullOrWhiteSpace(order.GiftId) ? null : await _gifts.GetAsync(order.GiftId, ct),
            Edit = OrderEditModel.FromOrder(order)
        });
    }

    [HttpPost("orders/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditOrder(string id, OrderEditModel edit, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (!DateOnly.TryParse(edit.DeliveryDate, out var delivery) || !DateOnly.TryParse(edit.PickupDate, out var pickup))
        {
            TempData["Error"] = "Both dates need to be valid.";
            return RedirectToAction(nameof(Order), new { id });
        }

        if (pickup <= delivery)
        {
            TempData["Error"] = "Pickup has to be after delivery.";
            return RedirectToAction(nameof(Order), new { id });
        }

        var admin = await RequireUserAsync();
        var changes = new List<string>();

        void Track(string label, string? before, string? after)
        {
            if (!string.Equals(before ?? "", after ?? "", StringComparison.Ordinal))
            {
                changes.Add($"{label}: {(string.IsNullOrWhiteSpace(before) ? "(blank)" : before)} \u2192 {(string.IsNullOrWhiteSpace(after) ? "(blank)" : after)}");
            }
        }

        Track("Delivery date", order.DeliveryDate, edit.DeliveryDate);
        Track("Delivery window", order.DeliveryWindow, edit.DeliveryWindow);
        Track("Pickup date", order.PickupDate, edit.PickupDate);
        Track("Pickup window", order.PickupWindow, edit.PickupWindow);
        Track("Address", order.DeliveryAddressLine1, edit.AddressLine1);
        Track("City", order.DeliveryCity, edit.City);
        Track("ZIP", order.DeliveryZip, edit.Zip);
        Track("Name", order.CustomerName, edit.CustomerName);
        Track("Phone", order.CustomerPhone, edit.CustomerPhone);
        Track("Totes with lids", order.CrateCount.ToString(), edit.CrateCount.ToString());
        Track("Custom-fit dollies", order.DollyCount.ToString(), edit.DollyCount.ToString());

        order.DeliveryDate = edit.DeliveryDate;
        order.DeliveryWindow = DeliveryWindows.Normalize(edit.DeliveryWindow);
        order.PickupDate = edit.PickupDate;
        order.PickupWindow = DeliveryWindows.Normalize(edit.PickupWindow);
        order.DeliveryAddressLine1 = edit.AddressLine1;
        order.DeliveryAddressLine2 = edit.AddressLine2;
        order.DeliveryCity = edit.City;
        order.DeliveryZip = edit.Zip;
        order.CustomerName = edit.CustomerName;
        order.CustomerPhone = edit.CustomerPhone;
        order.CrateCount = edit.CrateCount;
        order.DollyCount = edit.DollyCount;

        // Keep the zone label honest, since the surcharge was based on it.
        var zone = (await _regions.GetByIdAsync(order.RegionId, ct))?.FindZoneForZip(order.DeliveryZip);
        if (zone is not null)
        {
            order.ZoneName = zone.Name;
        }

        if (changes.Count > 0)
        {
            order.Notes.Add(new OrderNote
            {
                Body = "Edited by admin. " + string.Join("; ", changes),
                AuthorName = admin.DisplayName,
                AuthorUserId = admin.Id
            });
        }

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);

        TempData["Success"] = changes.Count > 0
            ? $"Saved {changes.Count} change{(changes.Count == 1 ? "" : "s")}."
            : "Nothing changed.";

        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/note")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddNote(string id, string body, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return RedirectToAction(nameof(Order), new { id });
        }

        var admin = await RequireUserAsync();

        order.Notes.Add(new OrderNote
        {
            Body = body.Trim(),
            AuthorName = admin.DisplayName,
            AuthorUserId = admin.Id
        });

        await _orders.SaveAsync(order, ct);

        TempData["Success"] = "Note added.";
        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(string id, OrderStatus status, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (status == OrderStatus.Cancelled)
        {
            // Cancelling has side effects, so it goes through the dedicated action.
            return RedirectToAction(nameof(Order), new { id });
        }

        var admin = await RequireUserAsync();
        var previous = order.Status;
        order.Status = status;

        if (status == OrderStatus.Delivered && order.DeliveredAtUtc is null)
        {
            order.DeliveredAtUtc = DateTime.UtcNow;
        }

        if (status == OrderStatus.Completed && order.PickedUpAtUtc is null)
        {
            order.PickedUpAtUtc = DateTime.UtcNow;
        }

        order.Notes.Add(new OrderNote
        {
            Body = $"Status changed from {StatusBadge.LabelFor(previous)} to {StatusBadge.LabelFor(status)}.",
            AuthorName = admin.DisplayName,
            AuthorUserId = admin.Id
        });

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);

        TempData["Success"] = $"Status set to {StatusBadge.LabelFor(status)}.";
        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelOrder(string id, string? reason, bool notifyCustomer, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            TempData["Info"] = "That order is already cancelled.";
            return RedirectToAction(nameof(Order), new { id });
        }

        var admin = await RequireUserAsync();
        var explanation = string.IsNullOrWhiteSpace(reason) ? "Cancelled by staff" : reason.Trim();

        order.Status = OrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancellationReason = explanation;
        order.Notes.Add(new OrderNote
        {
            Body = $"Cancelled by admin. {explanation}",
            AuthorName = admin.DisplayName,
            AuthorUserId = admin.Id
        });

        await _orders.SaveAsync(order, ct);
        await _inventory.GetAssessmentAsync(order.RegionId, reconcileTasks: true, ct);

        if (notifyCustomer)
        {
            await _notifier.SendCancellationAsync(order, explanation, ct);
        }

        // Refunds go through Stripe by hand: a partial refund is a judgement call, not a rule.
        TempData["Success"] = order.AmountPaidCents > 0
            ? $"Order cancelled. {Money.Format(order.AmountPaidCents)} was collected \u2014 issue any refund in Stripe."
            : "Order cancelled.";

        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/extend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendOrder(string id, int additionalWeeks, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        var admin = await RequireUserAsync();
        var result = await _extensions.ExtendAsync(order, additionalWeeks, admin.Id, ct);

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/damages/charge")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChargeDamages(string id, string[] damageIds, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        var admin = await RequireUserAsync();
        var result = await _damages.ApproveAndChargeAsync(order, damageIds ?? Array.Empty<string>(), admin, ct);

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/damages/waive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> WaiveDamages(string id, string[] damageIds, string? reason, CancellationToken ct)
    {
        var order = await LoadInScopeAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }

        var admin = await RequireUserAsync();
        var result = await _damages.WaiveAsync(order, damageIds ?? Array.Empty<string>(), admin, reason, ct);

        TempData[result.Success ? "Success" : "Error"] = result.Message;
        return RedirectToAction(nameof(Order), new { id });
    }

    [HttpPost("orders/{id}/damages/add")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddDamage(
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
            TempData["Error"] = "Pick what to charge for.";
            return RedirectToAction(nameof(Order), new { id });
        }

        var admin = await RequireUserAsync();
        var region = await _regions.GetByIdAsync(order.RegionId, ct);
        var unit = DamageKinds.UnitAmountCents(damageKind.Code, order.Terms, region?.DamageFees ?? new DamageFeeSchedule());

        order.Damages.Add(new DamageLine
        {
            Kind = damageKind.Code,
            Quantity = damageKind.PerUnit ? Math.Max(1, quantity) : 1,
            UnitAmountCents = unit,
            Description = description?.Trim() ?? string.Empty,
            Status = DamageChargeStatus.PendingReview,
            ReportedByUserId = admin.Id,
            ReportedByName = admin.DisplayName
        });

        await _orders.SaveAsync(order, ct);

        TempData["Success"] = "Charge queued. Approve it below to put it on the card.";
        return RedirectToAction(nameof(Order), new { id });
    }

    // ---------- users ----------

    [HttpGet("users")]
    public async Task<IActionResult> Users(string? region, string? role, string? search, CancellationToken ct)
    {
        ViewData["Title"] = "Users";

        var (scoped, canSwitch) = await ResolveRegionAsync(region, ct);
        var isPlatformAdmin = User.IsInRole(Roles.SaaSAdmin);

        var users = isPlatformAdmin && string.IsNullOrWhiteSpace(region)
            ? await _data.GetAllUsersAsync(ct)
            : await _data.GetUsersInRegionAsync(scoped?.Id ?? string.Empty, ct);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            users = users.Where(u =>
                    (u.Email ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (u.FullName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase)
                    || (u.CompanyName ?? "").Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var rows = new List<AdminUserRow>();
        foreach (var user in users.OrderBy(u => u.Email))
        {
            var roles = (await _userManager.GetRolesAsync(user)).ToList();

            if (!string.IsNullOrWhiteSpace(role) && !roles.Contains(role, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new AdminUserRow
            {
                User = user,
                Roles = roles,
                CanImpersonate = isPlatformAdmin
                                 || (!roles.Contains(Roles.SaaSAdmin) && !roles.Contains(Roles.RegionalAdmin))
            });
        }

        return View(new AdminUserListViewModel
        {
            Users = rows,
            Region = scoped,
            AllRegions = await _regions.GetAllAsync(ct),
            CanSwitchRegion = canSwitch,
            RoleFilter = role,
            Search = search,
            AssignableRoles = isPlatformAdmin
                ? Roles.All
                : new[] { Roles.Customer, Roles.Realtor, Roles.Worker }
        });
    }

    [HttpPost("users/{userId}/roles")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(string userId, string role, CancellationToken ct)
    {
        var target = await _userManager.FindByIdAsync(userId);
        if (target is null || !await CanManageAsync(target))
        {
            return NotFound();
        }

        if (!Roles.All.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            TempData["Error"] = "That isn't a role.";
            return RedirectToAction(nameof(Users));
        }

        // Only a platform admin can mint privileged accounts.
        if ((role == Roles.SaaSAdmin || role == Roles.RegionalAdmin) && !User.IsInRole(Roles.SaaSAdmin))
        {
            TempData["Error"] = "Only a platform admin can grant admin roles.";
            return RedirectToAction(nameof(Users));
        }

        var existing = await _userManager.GetRolesAsync(target);
        if (existing.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(target, existing);
        }

        await _userManager.AddToRoleAsync(target, role);

        _logger.LogWarning("{Admin} set {Target} to role {Role}", User.Identity?.Name, target.Email, role);
        TempData["Success"] = $"{target.DisplayName} is now a {role}.";

        return RedirectToAction(nameof(Users));
    }

    [HttpPost("users/{userId}/region")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetUserRegion(string userId, string regionId, CancellationToken ct)
    {
        var target = await _userManager.FindByIdAsync(userId);
        if (target is null || !await CanManageAsync(target))
        {
            return NotFound();
        }

        var region = await _regions.GetByIdAsync(regionId, ct);
        if (region is null)
        {
            TempData["Error"] = "That region doesn't exist.";
            return RedirectToAction(nameof(Users));
        }

        target.RegionId = region.Id;
        await _userManager.UpdateAsync(target);

        TempData["Success"] = $"{target.DisplayName} moved to {region.Name}.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost("users/{userId}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUser(string userId, CancellationToken ct)
    {
        var target = await _userManager.FindByIdAsync(userId);
        if (target is null || !await CanManageAsync(target))
        {
            return NotFound();
        }

        var self = await RequireUserAsync();
        if (target.Id == self.Id)
        {
            TempData["Error"] = "You can't disable your own account.";
            return RedirectToAction(nameof(Users));
        }

        target.IsDisabled = !target.IsDisabled;
        await _userManager.UpdateAsync(target);

        _logger.LogWarning(
            "{Admin} {Action} account {Target}",
            self.Email, target.IsDisabled ? "disabled" : "re-enabled", target.Email);

        TempData["Success"] = target.IsDisabled
            ? $"{target.DisplayName} can no longer sign in."
            : $"{target.DisplayName} can sign in again.";

        return RedirectToAction(nameof(Users));
    }

    // ---------- helpers ----------

    private static List<string> NormalizeWindows(IEnumerable<string>? windows)
        => (windows ?? Array.Empty<string>())
            .Where(DeliveryWindows.IsValid)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(window => Array.IndexOf(DeliveryWindows.All, window))
            .ToList();

    private static bool IsOverduePickup(RentalOrder order, DateOnly today)
        => order.PickedUpAtUtc is null
           && order.Status is OrderStatus.Delivered or OrderStatus.OutForPickup
           && string.Compare(order.PickupDate, today.ToString("yyyy-MM-dd"), StringComparison.Ordinal) < 0;

    private static List<RevenuePoint> BuildDailySeries(List<RentalOrder> orders, DateOnly today, int days)
    {
        var points = new List<RevenuePoint>(days);

        for (var offset = days - 1; offset >= 0; offset--)
        {
            var day = today.AddDays(-offset);
            var dayOrders = orders.Where(o => DateOnly.FromDateTime(o.CreatedAtUtc) == day).ToList();
            points.Add(new RevenuePoint(day.ToString("MMM d"), dayOrders.Sum(o => o.AmountPaidCents), dayOrders.Count));
        }

        return points;
    }

    private static List<RevenuePoint> BuildMonthlySeries(List<RentalOrder> orders, DateOnly monthStart, int months)
    {
        var points = new List<RevenuePoint>(months);

        for (var offset = months - 1; offset >= 0; offset--)
        {
            var start = monthStart.AddMonths(-offset);
            var end = start.AddMonths(1);
            var monthOrders = orders
                .Where(o => o.CreatedAtUtc >= start.ToDateTime(TimeOnly.MinValue)
                            && o.CreatedAtUtc < end.ToDateTime(TimeOnly.MinValue))
                .ToList();

            points.Add(new RevenuePoint(start.ToString("MMM yy"), monthOrders.Sum(o => o.AmountPaidCents), monthOrders.Count));
        }

        return points;
    }

    private async Task<(Region? Region, bool CanSwitch)> ResolveRegionAsync(string? requested, CancellationToken ct)
    {
        var canSwitch = User.IsInRole(Roles.SaaSAdmin);

        if (canSwitch)
        {
            return (string.IsNullOrWhiteSpace(requested)
                ? await _regions.GetDefaultAsync(ct)
                : await _regions.GetByIdAsync(requested, ct) ?? await _regions.GetDefaultAsync(ct), true);
        }

        // A regional admin is pinned to their own market regardless of what the query string says.
        var admin = await RequireUserAsync();
        var own = string.IsNullOrWhiteSpace(admin.RegionId)
            ? await _regions.GetDefaultAsync(ct)
            : await _regions.GetByIdAsync(admin.RegionId, ct);

        return (own, false);
    }

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

        var admin = await RequireUserAsync();
        return order.RegionId == admin.RegionId ? order : null;
    }

    private async Task<bool> CanManageAsync(ApplicationUser target)
    {
        if (User.IsInRole(Roles.SaaSAdmin))
        {
            return true;
        }

        var admin = await RequireUserAsync();
        if (string.IsNullOrWhiteSpace(admin.RegionId) || target.RegionId != admin.RegionId)
        {
            return false;
        }

        // A regional admin cannot edit a peer or a platform admin.
        var targetRoles = await _userManager.GetRolesAsync(target);
        return !targetRoles.Contains(Roles.SaaSAdmin) && !targetRoles.Contains(Roles.RegionalAdmin);
    }

    private async Task<ApplicationUser> RequireUserAsync()
        => await _userManager.GetUserAsync(User)
           ?? throw new InvalidOperationException("Signed in but the user record is missing.");
}
