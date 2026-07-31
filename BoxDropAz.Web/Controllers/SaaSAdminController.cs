using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;
using BoxDropAz.Web.Models.Admin;
using BoxDropAz.Web.Models.SaaSAdmin;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// Platform-wide administration: the markets themselves, what they sell, and how billing is
/// behaving. A regional admin never reaches here.
/// </summary>
[Authorize(Policy = "SaaSAdmin")]
[Route("saasadmin")]
public sealed class SaaSAdminController : Controller
{
    private readonly IRegionService _regions;
    private readonly ICatalogService _catalog;
    private readonly IOrderService _orders;
    private readonly ISubscriptionService _subscriptions;
    private readonly IStripeEventStore _stripeEvents;
    private readonly DynamoDbDataHelper _data;
    private readonly ILogger<SaaSAdminController> _logger;

    public SaaSAdminController(
        IRegionService regions,
        ICatalogService catalog,
        IOrderService orders,
        ISubscriptionService subscriptions,
        IStripeEventStore stripeEvents,
        DynamoDbDataHelper data,
        ILogger<SaaSAdminController> logger)
    {
        _regions = regions;
        _catalog = catalog;
        _orders = orders;
        _subscriptions = subscriptions;
        _stripeEvents = stripeEvents;
        _data = data;
        _logger = logger;
    }

    // ---------- rollup ----------

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewData["Title"] = "Platform overview";

        var regions = await _regions.GetAllAsync(ct);
        var today = DeliveryWindows.TodayInArizona();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var lastMonthStart = monthStart.AddMonths(-1);
        var windowStart = monthStart.AddMonths(-12);

        var model = new PlatformDashboardViewModel();
        var combined = new List<RentalOrder>();

        foreach (var region in regions)
        {
            var orders = await _orders.GetCreatedBetweenAsync(
                region.Id,
                windowStart.ToDateTime(TimeOnly.MinValue),
                today.AddDays(1).ToDateTime(TimeOnly.MinValue),
                ct);

            var billable = orders
                .Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.PendingPayment)
                .ToList();

            combined.AddRange(billable);

            var thisMonth = billable.Where(o => o.CreatedAtUtc >= monthStart.ToDateTime(TimeOnly.MinValue)).ToList();
            var lastMonth = billable
                .Where(o => o.CreatedAtUtc >= lastMonthStart.ToDateTime(TimeOnly.MinValue)
                            && o.CreatedAtUtc < monthStart.ToDateTime(TimeOnly.MinValue))
                .ToList();

            var packages = await _catalog.GetAllPackagesAsync(region.Id, ct);

            model.Regions.Add(new RegionRollup
            {
                Region = region,
                RevenueThisMonthCents = thisMonth.Sum(o => o.AmountPaidCents),
                RevenueLastMonthCents = lastMonth.Sum(o => o.AmountPaidCents),
                OrdersThisMonth = thisMonth.Count,
                ActiveRentals = billable.Count(o => o.IsActiveRental),
                GiftOrdersThisMonth = thisMonth.Count(o => o.Source == OrderSource.RealtorGift),
                PackageCount = packages.Count,
                ZoneCount = region.DeliveryZones.Count
            });

            model.RevenueByRegion[region.Name] = BuildMonthlySeries(billable, monthStart, 12);
        }

        model.MonthlyRevenue = BuildMonthlySeries(combined, monthStart, 12);

        var subscriptions = await _subscriptions.GetAllAsync(ct);
        var active = subscriptions.Where(s => s.IsActive).ToList();

        model.ActiveSubscriptions = active.Count;
        model.MonthlyRecurringCents = active.Sum(s => RealtorPlan.FromId(s.PlanId)?.MonthlyPriceCents ?? 0);
        model.OutstandingCreditCents = subscriptions.Sum(s => s.CreditBalanceCents);
        model.UserCount = (await _data.GetAllUsersAsync(ct)).Count;

        return View(model);
    }

    // ---------- regions ----------

    [HttpGet("regions")]
    public async Task<IActionResult> Regions(CancellationToken ct)
    {
        ViewData["Title"] = "Regions";

        var regions = await _regions.GetAllAsync(ct);
        var model = new RegionListViewModel { Regions = regions };

        foreach (var region in regions)
        {
            model.PackageCounts[region.Id] = (await _catalog.GetAllPackagesAsync(region.Id, ct)).Count;
            model.OrderCounts[region.Id] = (await _orders.GetCreatedBetweenAsync(
                region.Id,
                DateTime.UtcNow.AddYears(-5),
                DateTime.UtcNow.AddDays(1),
                ct)).Count;
        }

        return View(model);
    }

    [HttpGet("regions/new")]
    public IActionResult NewRegion()
    {
        ViewData["Title"] = "New region";
        ViewData["IsNew"] = true;

        return View("RegionForm", new RegionEditModel
        {
            Zones = new List<ZoneEditModel>
            {
                // A region without at least one zone cannot take a booking, so seed the free one.
                new() { Name = "Zone A", SurchargeCents = 0 }
            }
        });
    }

    [HttpGet("regions/{id}")]
    public async Task<IActionResult> EditRegion(string id, CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(id, ct);
        if (region is null)
        {
            return NotFound();
        }

        ViewData["Title"] = region.Name;
        ViewData["IsNew"] = false;
        ViewData["PackageCount"] = (await _catalog.GetAllPackagesAsync(region.Id, ct)).Count;
        ViewData["OrderCount"] = (await _orders.GetCreatedBetweenAsync(
            region.Id,
            DateTime.UtcNow.AddYears(-5),
            DateTime.UtcNow.AddDays(1),
            ct)).Count;

        return View("RegionForm", RegionEditModel.FromRegion(region));
    }

    [HttpPost("regions/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRegion(RegionEditModel form, CancellationToken ct)
    {
        var isNew = string.IsNullOrWhiteSpace(form.Id);

        // Blank rows are how the form lets you delete a zone, so drop them before validating.
        form.Zones = form.Zones
            .Where(z => !string.IsNullOrWhiteSpace(z.Name) || !string.IsNullOrWhiteSpace(z.ZipCodes))
            .ToList();

        if (form.Zones.Count == 0)
        {
            ModelState.AddModelError(nameof(form.Zones), "A region needs at least one delivery zone.");
        }
        else if (form.Zones.Any(z => string.IsNullOrWhiteSpace(z.Name)))
        {
            ModelState.AddModelError(nameof(form.Zones), "Every zone needs a name.");
        }

        var slugOwner = await _regions.GetBySlugAsync(form.Slug, ct);
        if (slugOwner is not null && slugOwner.Id != form.Id)
        {
            ModelState.AddModelError(nameof(form.Slug), $"{slugOwner.Name} already uses that slug.");
        }

        if (!ModelState.IsValid)
        {
            ViewData["IsNew"] = isNew;
            return View("RegionForm", form);
        }

        var region = isNew
            ? new Region { Id = Guid.NewGuid().ToString("N")[..12] }
            : await _regions.GetByIdAsync(form.Id!, ct);

        if (region is null)
        {
            return NotFound();
        }

        region.Name = form.Name.Trim();
        region.Slug = form.Slug.Trim().ToLowerInvariant();
        region.Description = form.Description?.Trim() ?? string.Empty;
        region.TimeZoneId = string.IsNullOrWhiteSpace(form.TimeZoneId) ? "US/Arizona" : form.TimeZoneId.Trim();
        region.SupportPhone = form.SupportPhone?.Trim() ?? string.Empty;
        region.IsActive = form.IsActive;
        region.UpdatedAtUtc = DateTime.UtcNow;

        region.DeliveryZones = form.Zones.Select(z => new DeliveryZone
        {
            Name = z.Name.Trim(),
            Cities = z.Cities?.Trim() ?? string.Empty,
            ZipCodes = ParseZips(z.ZipCodes),
            SurchargeCents = z.SurchargeCents
        }).ToList();

        region.DamageFees = new DamageFeeSchedule
        {
            CrateReplacementCents = form.CrateReplacementCents,
            DollyReplacementCents = form.DollyReplacementCents,
            MissedPickupCents = form.MissedPickupCents,
            DeepCleanPerCrateCents = form.DeepCleanPerCrateCents
        };

        await _regions.SaveAsync(region, ct);

        _logger.LogInformation("{Admin} saved region {Region}", User.Identity?.Name, region.Name);

        TempData["Success"] = isNew
            ? $"{region.Name} created. Add packages before it can take bookings."
            : $"{region.Name} saved.";

        return isNew
            ? RedirectToAction(nameof(Packages), new { regionId = region.Id })
            : RedirectToAction(nameof(EditRegion), new { id = region.Id });
    }

    [HttpPost("regions/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRegion(string id, CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(id, ct);
        if (region is null)
        {
            return NotFound();
        }

        // Orders reference the region for pricing history and the worker manifest, so deleting a
        // region with any order would orphan them. Deactivating is the correct move there.
        var orderCount = (await _orders.GetCreatedBetweenAsync(
            region.Id,
            DateTime.UtcNow.AddYears(-5),
            DateTime.UtcNow.AddDays(1),
            ct)).Count;

        if (orderCount > 0)
        {
            TempData["Error"] =
                $"{region.Name} has {orderCount} order{(orderCount == 1 ? "" : "s")}. " +
                "Turn off bookings instead of deleting it.";
            return RedirectToAction(nameof(EditRegion), new { id });
        }

        foreach (var package in await _catalog.GetAllPackagesAsync(region.Id, ct))
        {
            await _catalog.DeletePackageAsync(region.Id, package.PackageId, ct);
        }

        await _regions.DeleteAsync(region.Id, ct);

        _logger.LogWarning("{Admin} deleted region {Region}", User.Identity?.Name, region.Name);
        TempData["Success"] = $"{region.Name} deleted.";

        return RedirectToAction(nameof(Regions));
    }

    // ---------- packages ----------

    [HttpGet("packages")]
    public async Task<IActionResult> Packages(string? regionId, CancellationToken ct)
    {
        var regions = await _regions.GetAllAsync(ct);
        var region = (string.IsNullOrWhiteSpace(regionId)
            ? regions.FirstOrDefault()
            : regions.FirstOrDefault(r => r.Id == regionId)) ?? regions.FirstOrDefault();

        if (region is null)
        {
            TempData["Info"] = "Create a region before adding packages.";
            return RedirectToAction(nameof(Regions));
        }

        ViewData["Title"] = $"Packages \u2014 {region.Name}";

        return View(new PackageListViewModel
        {
            Region = region,
            AllRegions = regions,
            Packages = (await _catalog.GetAllPackagesAsync(region.Id, ct))
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.CrateCount)
                .ToList()
        });
    }

    [HttpGet("packages/new")]
    public async Task<IActionResult> NewPackage(string regionId, CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(regionId, ct);
        if (region is null)
        {
            return NotFound();
        }

        ViewData["Title"] = "New package";
        ViewData["IsNew"] = true;
        ViewData["Region"] = region;

        var existing = await _catalog.GetAllPackagesAsync(region.Id, ct);

        return View("PackageForm", new PackageEditModel
        {
            RegionId = region.Id,
            SortOrder = existing.Count == 0 ? 10 : existing.Max(p => p.SortOrder) + 10,
            IncludedItems = "Delivery and pickup\n27-gallon totes with snap-fit lids\nCustom-fit dollies\n1 package of 300 color-coded 3x5 cards\n7 day rental"
        });
    }

    [HttpGet("packages/{regionId}/{packageId}")]
    public async Task<IActionResult> EditPackage(string regionId, string packageId, CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(regionId, ct);
        var package = await _catalog.GetPackageAsync(regionId, packageId, ct);

        if (region is null || package is null)
        {
            return NotFound();
        }

        ViewData["Title"] = package.Name;
        ViewData["IsNew"] = false;
        ViewData["Region"] = region;

        return View("PackageForm", PackageEditModel.FromPackage(package));
    }

    [HttpPost("packages/save")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SavePackage(PackageEditModel form, CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(form.RegionId ?? string.Empty, ct);
        if (region is null)
        {
            return NotFound();
        }

        var isNew = string.IsNullOrWhiteSpace(form.PackageId);

        if (form.ExtraWeekPriceCents > form.BasePriceCents && form.BasePriceCents > 0)
        {
            ModelState.AddModelError(nameof(form.ExtraWeekPriceCents),
                "An extra week costs more than the first week, which is almost certainly a typo.");
        }

        ViewData["IsNew"] = isNew;
        ViewData["Region"] = region;

        if (!ModelState.IsValid)
        {
            return View("PackageForm", form);
        }

        var packageId = isNew ? Slugify(form.Name) : form.PackageId!;

        if (isNew && await _catalog.GetPackageAsync(region.Id, packageId, ct) is not null)
        {
            ModelState.AddModelError(nameof(form.Name), "A package with that name already exists in this region.");
            return View("PackageForm", form);
        }

        await _catalog.SavePackageAsync(new CratePackage
        {
            RegionId = region.Id,
            PackageId = packageId,
            Name = form.Name.Trim(),
            Subtitle = form.Subtitle?.Trim() ?? string.Empty,
            CrateCount = form.CrateCount,
            DollyCount = form.DollyCount,
            BasePriceCents = form.BasePriceCents,
            ExtraWeekPriceCents = form.ExtraWeekPriceCents,
            IncludedItems = SplitLines(form.IncludedItems),
            Badge = string.IsNullOrWhiteSpace(form.Badge) ? null : form.Badge.Trim(),
            SortOrder = form.SortOrder,
            IsActive = form.IsActive,
            UpdatedAtUtc = DateTime.UtcNow
        }, ct);

        TempData["Success"] = $"{form.Name} saved for {region.Name}.";
        return RedirectToAction(nameof(Packages), new { regionId = region.Id });
    }

    [HttpPost("packages/{regionId}/{packageId}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePackage(string regionId, string packageId, CancellationToken ct)
    {
        var package = await _catalog.GetPackageAsync(regionId, packageId, ct);
        if (package is null)
        {
            return NotFound();
        }

        // Past orders keep a copy of the name and price, so removing the record is safe.
        await _catalog.DeletePackageAsync(regionId, packageId, ct);

        TempData["Success"] = $"{package.Name} removed.";
        return RedirectToAction(nameof(Packages), new { regionId });
    }

    [HttpPost("packages/{regionId}/{packageId}/toggle")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TogglePackage(string regionId, string packageId, CancellationToken ct)
    {
        var package = await _catalog.GetPackageAsync(regionId, packageId, ct);
        if (package is null)
        {
            return NotFound();
        }

        package.IsActive = !package.IsActive;
        package.UpdatedAtUtc = DateTime.UtcNow;
        await _catalog.SavePackageAsync(package, ct);

        TempData["Success"] = package.IsActive
            ? $"{package.Name} is bookable again."
            : $"{package.Name} is hidden from new bookings.";

        return RedirectToAction(nameof(Packages), new { regionId });
    }

    // ---------- stripe events ----------

    [HttpGet("stripe-events")]
    public async Task<IActionResult> StripeEvents(string? type, string? outcome, CancellationToken ct)
    {
        ViewData["Title"] = "Stripe events";

        var events = await _stripeEvents.GetRecentAsync(400, ct);

        var model = new StripeEventsViewModel
        {
            TypeFilter = type,
            OutcomeFilter = outcome,
            KnownTypes = events.Select(e => e.EventType).Distinct().OrderBy(t => t).ToList()
        };

        if (!string.IsNullOrWhiteSpace(type))
        {
            events = events.Where(e => string.Equals(e.EventType, type, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (!string.IsNullOrWhiteSpace(outcome))
        {
            events = outcome switch
            {
                "failed" => events.Where(e => !string.IsNullOrWhiteSpace(e.ErrorMessage)).ToList(),
                "unprocessed" => events.Where(e => e.ProcessedAtUtc is null).ToList(),
                _ => events.Where(e => e.ProcessedAtUtc is not null && string.IsNullOrWhiteSpace(e.ErrorMessage)).ToList()
            };
        }

        model.Events = events.OrderByDescending(e => e.ReceivedAtUtc).ToList();

        return View(model);
    }

    // ---------- helpers ----------

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

    /// <summary>Accepts ZIPs pasted in any shape: commas, spaces, or one per line.</summary>
    private static List<string> ParseZips(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(z => z.Trim())
                .Where(z => z.Length == 5 && z.All(char.IsDigit))
                .Distinct()
                .OrderBy(z => z)
                .ToList();

    private static List<string> SplitLines(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();

    private static string Slugify(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray());

        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }

        slug = slug.Trim('-');

        return slug.Length == 0 ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}
