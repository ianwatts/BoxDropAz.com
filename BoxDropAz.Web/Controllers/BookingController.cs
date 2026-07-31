using System.Security.Cryptography;
using System.Text;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Booking;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Models.Payments;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxDropAz.Web.Controllers;

public sealed class BookingController : Controller
{
    private readonly IRegionService _regions;
    private readonly ICatalogService _catalog;
    private readonly IOrderService _orders;
    private readonly IGiftService _gifts;
    private readonly IStripeGateway _stripe;
    private readonly PricingService _pricing;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly OrderNotifier _notifier;
    private readonly OrderCheckoutService _checkout;
    private readonly IConfiguration _config;
    private readonly ILogger<BookingController> _logger;

    public BookingController(
        IRegionService regions,
        ICatalogService catalog,
        IOrderService orders,
        IGiftService gifts,
        IStripeGateway stripe,
        PricingService pricing,
        UserManager<ApplicationUser> userManager,
        OrderNotifier notifier,
        OrderCheckoutService checkout,
        IConfiguration config,
        ILogger<BookingController> logger)
    {
        _regions = regions;
        _catalog = catalog;
        _orders = orders;
        _gifts = gifts;
        _stripe = stripe;
        _pricing = pricing;
        _userManager = userManager;
        _notifier = notifier;
        _checkout = checkout;
        _config = config;
        _logger = logger;
    }

    /// <summary>Step 1: choose a region and a bundle.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(string? region, string? package, string? zip, CancellationToken ct)
    {
        ViewData["Title"] = "Book your moving totes";

        var all = await _regions.GetActiveAsync(ct);

        // A ZIP typed on the marketing site picks the region for us.
        Region? selected = null;
        if (!string.IsNullOrWhiteSpace(zip))
        {
            (selected, _) = await _regions.ResolveZipAsync(zip, ct);
        }

        selected ??= all.FirstOrDefault(r => r.Id == region || string.Equals(r.Slug, region, StringComparison.OrdinalIgnoreCase))
                     ?? await _regions.GetDefaultAsync(ct);

        if (selected is not null && !string.IsNullOrWhiteSpace(package))
        {
            var chosen = await _catalog.GetPackageAsync(selected.Id, package, ct);
            if (chosen is not null)
            {
                return RedirectToAction(nameof(Schedule), new { region = selected.Id, package = chosen.PackageId, zip });
            }
        }

        return View(new PackageSelectViewModel
        {
            Region = selected,
            AllRegions = all,
            Packages = selected is null ? new List<CratePackage>() : await _catalog.GetPackagesAsync(selected.Id, ct),
            Zip = zip
        });
    }

    /// <summary>Step 2: dates, address, and extras.</summary>
    [HttpGet]
    public async Task<IActionResult> Schedule(string? region, string? package, string? zip, string? gift, CancellationToken ct)
    {
        ViewData["Title"] = "Schedule your delivery";

        var giftOrder = await LoadClaimableGiftAsync(gift, ct);
        if (!string.IsNullOrWhiteSpace(gift) && giftOrder is null)
        {
            TempData["Error"] = "That gift link is no longer valid. It may have already been claimed or cancelled.";
            return RedirectToAction(nameof(Index));
        }

        var regionId = giftOrder?.RegionId ?? region;
        var selectedRegion = await ResolveRegionAsync(regionId, ct);
        if (selectedRegion is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var packages = await _catalog.GetPackagesAsync(selectedRegion.Id, ct);
        var selectedPackage = packages.FirstOrDefault(p => p.PackageId == package) ?? packages.FirstOrDefault();
        if (selectedPackage is null)
        {
            TempData["Error"] = "We don't have any bundles available in that area yet.";
            return RedirectToAction(nameof(Index));
        }

        var earliestDelivery = FindFirstBookableDeliveryDate(selectedRegion, 1);
        var deliveryWindows = SchedulingRules.GetAvailableWindows(
            selectedRegion, earliestDelivery, ScheduleOperations.Delivery);
        var pickupWindows = SchedulingRules.GetAvailableWindows(
            selectedRegion, earliestDelivery.AddDays(RentalTerms.BaseRentalDays), ScheduleOperations.Pickup);
        var form = new BookingFormModel
        {
            RegionId = selectedRegion.Id,
            PackageId = selectedPackage.PackageId,
            DeliveryDate = earliestDelivery.ToString("yyyy-MM-dd"),
            DeliveryWindow = deliveryWindows.FirstOrDefault() ?? DeliveryWindows.Default,
            PickupWindow = pickupWindows.FirstOrDefault() ?? DeliveryWindows.Default,
            Zip = zip ?? giftOrder?.PropertyZip ?? string.Empty,
            City = giftOrder?.PropertyCity ?? string.Empty,
            AddressLine1 = giftOrder?.PropertyAddressLine1 ?? string.Empty,
            FullName = giftOrder?.ClientName ?? string.Empty,
            Email = giftOrder?.ClientEmail ?? string.Empty,
            Phone = giftOrder?.ClientPhone ?? string.Empty,
            GiftToken = giftOrder?.ClaimToken
        };

        await PrefillFromSignedInUserAsync(form);

        return View(await BuildScheduleViewModelAsync(form, selectedRegion, packages, giftOrder, ct));
    }

    /// <summary>
    /// Resolves a ZIP to its zone so the browser can show the surcharge, and the coverage error,
    /// before the customer fills in the rest of the form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ZoneLookup(string regionId, string zip, CancellationToken ct)
    {
        var region = await ResolveRegionAsync(regionId, ct);
        var zone = region?.FindZoneForZip(zip);

        if (zone is null)
        {
            // Another region may cover it, which is worth telling them rather than a flat no.
            var (otherRegion, otherZone) = await _regions.ResolveZipAsync(zip, ct);
            if (otherRegion is not null && otherZone is not null)
            {
                return Json(new
                {
                    covered = true,
                    inSelectedRegion = false,
                    regionId = otherRegion.Id,
                    regionName = otherRegion.Name,
                    zoneName = otherZone.Name,
                    surchargeCents = otherZone.SurchargeCents
                });
            }

            return Json(new { covered = false });
        }

        return Json(new
        {
            covered = true,
            inSelectedRegion = true,
            regionId = region!.Id,
            regionName = region.Name,
            zoneName = zone.Name,
            surchargeCents = zone.SurchargeCents
        });
    }

    [HttpGet]
    public async Task<IActionResult> Availability(
        string regionId,
        string date,
        int weeks = 1,
        CancellationToken ct = default)
    {
        var region = await ResolveRegionAsync(regionId, ct);
        if (region is null || !DateOnly.TryParse(date, out var deliveryDate))
        {
            return BadRequest();
        }

        weeks = PricingService.ClampWeeks(weeks);
        var pickupDate = deliveryDate.AddDays(RentalTerms.BaseRentalDays * weeks);
        return Json(new
        {
            deliveryDate = deliveryDate.ToString("yyyy-MM-dd"),
            pickupDate = pickupDate.ToString("yyyy-MM-dd"),
            deliveryWindows = SchedulingRules.GetAvailableWindows(
                region, deliveryDate, ScheduleOperations.Delivery),
            pickupWindows = SchedulingRules.GetAvailableWindows(
                region, pickupDate, ScheduleOperations.Pickup)
        });
    }

    /// <summary>Step 3: confirm the quote and accept the rental agreement.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(BookingFormModel form, CancellationToken ct)
    {
        ViewData["Title"] = "Review your booking";

        var context = await ValidateAsync(form, ct);
        if (context is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Schedule),
                await BuildScheduleViewModelAsync(form, context.Region, context.Packages, context.Gift, ct));
        }

        return View(new ReviewViewModel
        {
            Form = form,
            Region = context.Region,
            Package = context.Package,
            Zone = context.Zone,
            Quote = context.Quote,
            DeliveryDate = context.DeliveryDate,
            PickupDate = context.PickupDate,
            GiftCreditCents = context.GiftCreditCents,
            GiftingAgentName = context.Gift?.RealtorName,
            StripeConfigured = _stripe.IsConfigured
        });
    }

    /// <summary>Step 4: persist the order and hand off to Stripe.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Checkout(BookingFormModel form, CancellationToken ct)
    {
        var context = await ValidateAsync(form, ct);
        if (context is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // The checkbox is the legal record of acceptance, so it is enforced here rather than
        // relying on the browser to have blocked the post.
        if (!form.AcceptTerms)
        {
            ModelState.AddModelError(nameof(form.AcceptTerms), "You need to accept the rental agreement before checking out.");
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Review), new ReviewViewModel
            {
                Form = form,
                Region = context.Region,
                Package = context.Package,
                Zone = context.Zone,
                Quote = context.Quote,
                DeliveryDate = context.DeliveryDate,
                PickupDate = context.PickupDate,
                GiftCreditCents = context.GiftCreditCents,
                GiftingAgentName = context.Gift?.RealtorName,
                StripeConfigured = _stripe.IsConfigured
            });
        }

        if (!_stripe.IsConfigured)
        {
            TempData["Error"] = "Online payment isn't available right now. Please call us and we'll book it for you.";
            return RedirectToAction(nameof(Review));
        }

        var (user, accountCreated) = await ResolveCustomerAsync(form, context.Region.Id, ct);
        var order = BuildOrder(form, context, user);

        await _orders.SaveAsync(order, ct);

        try
        {
            var customerId = await _stripe.EnsureCustomerAsync(user, ct);
            order.StripeCustomerId = customerId;

            var returnUrl = Url.Action(nameof(Complete), "Booking",
                new { orderId = order.OrderId, accountCreated }, Request.Scheme) + "&session_id={CHECKOUT_SESSION_ID}";
            var cancelUrl = Url.Action(nameof(Cancelled), "Booking", new { orderId = order.OrderId }, Request.Scheme)!;

            var metadata = new Dictionary<string, string>
            {
                ["kind"] = CheckoutKind.RentalOrder,
                ["orderId"] = order.OrderId,
                ["orderNumber"] = order.OrderNumber,
                ["userId"] = user.Id
            };

            if (context.Gift is not null)
            {
                metadata["giftId"] = context.Gift.GiftId;
            }

            Stripe.Checkout.Session session;
            if (context.Quote.IsFullyCoveredByCredit)
            {
                // Stripe rejects a zero-amount payment, but the agreement still requires a card
                // on file for extensions and damages, so switch to setup mode.
                metadata["kind"] = CheckoutKind.GiftSetup;
                session = await _stripe.CreateSetupSessionAsync(customerId, user.Id, returnUrl, metadata, ct);
            }
            else
            {
                session = await _stripe.CreatePaymentSessionAsync(
                    customerId, user.Id, BuildCheckoutLines(context), returnUrl, metadata, ct);
            }

            order.StripeCheckoutSessionId = session.Id;
            await _orders.SaveAsync(order, ct);

            return View("EmbeddedCheckout", new EmbeddedCheckoutViewModel
            {
                Title = context.Quote.IsFullyCoveredByCredit ? "Secure your booking" : "Complete payment",
                Description = $"Order {order.OrderNumber} · {context.Package.Name}",
                ClientSecret = session.ClientSecret,
                PublishableKey = _config["Stripe:PublishableKey"] ?? string.Empty,
                CancelUrl = cancelUrl,
                CancelLabel = "Cancel booking",
                SummaryTitle = "Due today",
                SummaryText = context.Quote.IsFullyCoveredByCredit
                    ? "Your gift credit covers the rental. Stripe will securely save a card for extensions or agreement fees."
                    : Money.Format(context.Quote.TotalDueCents)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start checkout for order {OrderId}", order.OrderId);
            TempData["Error"] = "We couldn't reach our payment provider. Nothing was charged. Please try again.";
            return RedirectToAction(nameof(Index));
        }
    }

    [HttpGet]
    public async Task<IActionResult> Complete(string orderId, string? session_id, bool accountCreated, CancellationToken ct)
    {
        ViewData["Title"] = "Booking confirmed";

        var order = await _orders.GetAsync(orderId, ct);
        if (order is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // The webhook is the authority, but it can land after the browser returns. Confirming
        // here too means the customer never sees a "pending" page for a payment that succeeded.
        if (order.Status == OrderStatus.PendingPayment && !string.IsNullOrWhiteSpace(session_id))
        {
            var session = await _stripe.GetSessionAsync(session_id, ct);
            var isPaid = session is not null &&
                         (session.PaymentStatus == "paid" || session.PaymentStatus == "no_payment_required" || session.Mode == "setup");

            if (isPaid && session is not null)
            {
                await _checkout.ConfirmFromSessionAsync(order, session, ct);
            }
        }

        return View(new BookingCompleteViewModel
        {
            Order = order,
            PaymentConfirmed = order.Status != OrderStatus.PendingPayment,
            AccountCreated = accountCreated
        });
    }

    [HttpGet]
    public async Task<IActionResult> Cancelled(string orderId, CancellationToken ct)
    {
        var order = await _orders.GetAsync(orderId, ct);
        if (order is { Status: OrderStatus.PendingPayment })
        {
            order.Status = OrderStatus.Cancelled;
            order.CancelledAtUtc = DateTime.UtcNow;
            order.CancellationReason = "Abandoned at checkout";
            await _orders.SaveAsync(order, ct);
        }

        TempData["Error"] = "Checkout was cancelled and nothing was charged. Your dates are still available.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- helpers ----------

    private sealed class BookingContext
    {
        public required Region Region { get; init; }
        public required CratePackage Package { get; init; }
        public required List<CratePackage> Packages { get; init; }
        public DeliveryZone? Zone { get; init; }
        public required RentalQuote Quote { get; init; }
        public DateOnly DeliveryDate { get; init; }
        public DateOnly PickupDate { get; init; }
        public GiftOrder? Gift { get; init; }
        public int GiftCreditCents { get; init; }
    }

    /// <summary>
    /// Re-derives everything priceable from server-side records. Nothing about the money comes
    /// from the posted form beyond quantities and choices.
    /// </summary>
    private async Task<BookingContext?> ValidateAsync(BookingFormModel form, CancellationToken ct)
    {
        var region = await ResolveRegionAsync(form.RegionId, ct);
        if (region is null)
        {
            return null;
        }

        var packages = await _catalog.GetPackagesAsync(region.Id, ct);
        var package = packages.FirstOrDefault(p => p.PackageId == form.PackageId);
        if (package is null)
        {
            return null;
        }

        var gift = await LoadClaimableGiftAsync(form.GiftToken, ct);
        if (!string.IsNullOrWhiteSpace(form.GiftToken) && gift is null)
        {
            ModelState.AddModelError(string.Empty, "That gift link is no longer valid.");
        }

        var zone = region.FindZoneForZip(form.Zip);
        if (zone is null)
        {
            ModelState.AddModelError(nameof(form.Zip), $"We don't deliver to {form.Zip} yet. Try another ZIP or contact us.");
        }

        var deliveryDate = form.ParseDeliveryDate();
        if (deliveryDate is null)
        {
            ModelState.AddModelError(nameof(form.DeliveryDate), "Choose a delivery date.");
        }
        else if (deliveryDate < SchedulingRules.EarliestDeliveryDate(region))
        {
            ModelState.AddModelError(nameof(form.DeliveryDate),
                $"The earliest we can deliver is {SchedulingRules.EarliestDeliveryDate(region):MMMM d, yyyy}.");
        }
        else if (deliveryDate > DeliveryWindows.LatestDeliveryDate())
        {
            ModelState.AddModelError(nameof(form.DeliveryDate), "That date is too far out to schedule yet.");
        }

        form.RentalWeeks = PricingService.ClampWeeks(form.RentalWeeks);
        if (deliveryDate is not null
            && !SchedulingRules.IsWindowAvailable(
                region, deliveryDate.Value, ScheduleOperations.Delivery, form.DeliveryWindow))
        {
            ModelState.AddModelError(nameof(form.DeliveryWindow),
                "That delivery window is unavailable. Choose another time.");
        }

        var candidateDelivery = deliveryDate ?? SchedulingRules.EarliestDeliveryDate(region);
        var candidatePickup = candidateDelivery.AddDays(RentalTerms.BaseRentalDays * form.RentalWeeks);
        if (!SchedulingRules.IsWindowAvailable(
                region, candidatePickup, ScheduleOperations.Pickup, form.PickupWindow))
        {
            ModelState.AddModelError(nameof(form.PickupWindow),
                "That pickup window is unavailable. Choose another time or delivery date.");
        }

        if (!form.PickupSameAsDelivery && string.IsNullOrWhiteSpace(form.PickupAddressLine1))
        {
            ModelState.AddModelError(nameof(form.PickupAddressLine1), "Enter the address we're collecting from.");
        }

        var giftCredit = gift?.GiftAmountCents ?? 0;
        var quote = _pricing.Quote(package, zone, form.RentalWeeks, form.ToAddOnLines(), giftCredit);

        var resolvedDelivery = deliveryDate ?? SchedulingRules.EarliestDeliveryDate(region);

        return new BookingContext
        {
            Region = region,
            Package = package,
            Packages = packages,
            Zone = zone,
            Quote = quote,
            DeliveryDate = resolvedDelivery,
            PickupDate = resolvedDelivery.AddDays(RentalTerms.BaseRentalDays * form.RentalWeeks),
            Gift = gift,
            GiftCreditCents = giftCredit
        };
    }

    private RentalOrder BuildOrder(BookingFormModel form, BookingContext context, ApplicationUser user)
    {
        var pickupSame = form.PickupSameAsDelivery;

        return new RentalOrder
        {
            OrderId = Guid.NewGuid().ToString("N"),
            OrderNumber = _orders.GenerateOrderNumber(),
            RegionId = context.Region.Id,
            UserId = user.Id,
            Status = OrderStatus.PendingPayment,
            Source = context.Gift is null ? OrderSource.Direct : OrderSource.RealtorGift,

            CustomerName = form.FullName,
            CustomerEmail = form.Email,
            CustomerPhone = form.Phone,

            DeliveryAddressLine1 = form.AddressLine1,
            DeliveryAddressLine2 = form.AddressLine2,
            DeliveryCity = form.City,
            DeliveryZip = form.Zip,
            PickupAddressLine1 = pickupSame ? form.AddressLine1 : form.PickupAddressLine1!,
            PickupAddressLine2 = pickupSame ? form.AddressLine2 : form.PickupAddressLine2,
            PickupCity = pickupSame ? form.City : form.PickupCity ?? form.City,
            PickupZip = pickupSame ? form.Zip : form.PickupZip ?? form.Zip,

            DeliveryDate = context.DeliveryDate.ToString("yyyy-MM-dd"),
            DeliveryWindow = form.DeliveryWindow,
            PickupDate = context.PickupDate.ToString("yyyy-MM-dd"),
            PickupWindow = form.PickupWindow,
            ZoneName = context.Zone?.Name ?? string.Empty,

            PackageId = context.Package.PackageId,
            PackageName = context.Package.Name,
            CrateCount = context.Package.CrateCount + form.ExtraCrateQty + form.WardrobeCrateQty,
            DollyCount = context.Package.DollyCount,
            RequiresIndexCard = true,
            RentalWeeks = form.RentalWeeks,

            PackageBaseCents = context.Quote.PackageBaseCents,
            ExtraWeeksCents = context.Quote.ExtraWeeksCents,
            ZoneSurchargeCents = context.Quote.ZoneSurchargeCents,
            AddOnsCents = context.Quote.AddOnsCents,
            GiftCreditAppliedCents = context.Quote.GiftCreditAppliedCents,
            TotalDueCents = context.Quote.TotalDueCents,
            AddOns = context.Quote.AddOns,

            GiftId = context.Gift?.GiftId,
            GiftingRealtorName = context.Gift?.RealtorName,
            GiftingRealtorCompany = context.Gift?.RealtorCompany,
            IncludeCoBrandingInsert = context.Gift?.IncludeCoBrandingInsert ?? false,

            Terms = new TermsAcceptance
            {
                TermsVersion = RentalTerms.CurrentVersion,
                AcceptedAtUtc = DateTime.UtcNow,
                AcceptedFromIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                AcceptedUserAgent = Request.Headers.UserAgent.ToString(),
                CrateReplacementCents = context.Region.DamageFees.CrateReplacementCents,
                DollyReplacementCents = context.Region.DamageFees.DollyReplacementCents,
                MissedPickupCents = context.Region.DamageFees.MissedPickupCents,
                DeepCleanPerCrateCents = context.Region.DamageFees.DeepCleanPerCrateCents
            },

            Notes = string.IsNullOrWhiteSpace(form.DeliveryNotes)
                ? new List<OrderNote>()
                : new List<OrderNote>
                {
                    new()
                    {
                        Body = form.DeliveryNotes,
                        AuthorName = form.FullName,
                        AuthorUserId = user.Id
                    }
                }
        };
    }

    private static List<CheckoutLine> BuildCheckoutLines(BookingContext context)
    {
        var lines = new List<CheckoutLine>
        {
            new(
                $"{context.Package.Name} moving tote bundle",
                $"{context.Package.CrateCount} totes with lids and {context.Package.DollyCount} custom-fit dollies for {RentalTerms.BaseRentalDays} days",
                context.Quote.PackageBaseCents,
                1)
        };

        if (context.Quote.ExtraWeeksCents > 0)
        {
            var extraWeeks = context.Quote.RentalWeeks - 1;
            lines.Add(new CheckoutLine(
                $"{extraWeeks} additional week{(extraWeeks == 1 ? "" : "s")}",
                "Extended rental period",
                context.Quote.ExtraWeeksCents,
                1));
        }

        if (context.Quote.ZoneSurchargeCents > 0)
        {
            lines.Add(new CheckoutLine(
                $"Delivery surcharge ({context.Zone?.Name})",
                "Round trip delivery and pickup outside Zone A",
                context.Quote.ZoneSurchargeCents,
                1));
        }

        foreach (var addOn in context.Quote.AddOns)
        {
            lines.Add(new CheckoutLine(addOn.Name, null, addOn.UnitAmountCents, addOn.Quantity));
        }

        return lines;
    }

    /// <summary>
    /// Finds or creates the account the order hangs off. Anonymous bookers get an account with an
    /// unusable password plus a set-password email, so the rental still has a home they can reach.
    /// </summary>
    private async Task<(ApplicationUser User, bool Created)> ResolveCustomerAsync(BookingFormModel form, string regionId, CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            var signedIn = await _userManager.GetUserAsync(User);
            if (signedIn is not null)
            {
                return (signedIn, false);
            }
        }

        var existing = await _userManager.FindByEmailAsync(form.Email);
        if (existing is not null)
        {
            return (existing, false);
        }

        var user = new ApplicationUser
        {
            UserName = form.Email,
            Email = form.Email,
            EmailConfirmed = true,
            FullName = form.FullName,
            PhoneNumber = form.Phone,
            RegionId = regionId
        };

        // Not a login credential: the account is only reachable through the emailed reset link.
        var placeholderPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)) + "aA1!";
        var result = await _userManager.CreateAsync(user, placeholderPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create an account for {form.Email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await _userManager.AddToRoleAsync(user, Roles.Customer);

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        var setPasswordUrl = Url.Action("ResetPassword", "Account", new { email = user.Email, code }, Request.Scheme)!;
        await _notifier.SendAccountSetupAsync(user.Email!, user.FullName ?? "there", setPasswordUrl, ct);

        return (user, true);
    }

    private async Task<Region?> ResolveRegionAsync(string? regionIdOrSlug, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(regionIdOrSlug))
        {
            return await _regions.GetDefaultAsync(ct);
        }

        var all = await _regions.GetActiveAsync(ct);
        return all.FirstOrDefault(r => r.Id == regionIdOrSlug)
               ?? all.FirstOrDefault(r => string.Equals(r.Slug, regionIdOrSlug, StringComparison.OrdinalIgnoreCase))
               ?? await _regions.GetDefaultAsync(ct);
    }

    private async Task<GiftOrder?> LoadClaimableGiftAsync(string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var gift = await _gifts.GetByClaimTokenAsync(token, ct);
        return gift is not null && gift.IsClaimable ? gift : null;
    }

    private async Task PrefillFromSignedInUserAsync(BookingFormModel form)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return;
        }

        form.FullName = string.IsNullOrWhiteSpace(form.FullName) ? user.FullName ?? "" : form.FullName;
        form.Email = string.IsNullOrWhiteSpace(form.Email) ? user.Email ?? "" : form.Email;
        form.Phone = string.IsNullOrWhiteSpace(form.Phone) ? user.PhoneNumber ?? "" : form.Phone;
    }

    private async Task<ScheduleViewModel> BuildScheduleViewModelAsync(
        BookingFormModel form,
        Region region,
        List<CratePackage> packages,
        GiftOrder? gift,
        CancellationToken ct)
    {
        var package = packages.FirstOrDefault(p => p.PackageId == form.PackageId) ?? packages.FirstOrDefault();
        var zone = region.FindZoneForZip(form.Zip);
        var giftCredit = gift?.GiftAmountCents ?? 0;
        var earliest = FindFirstBookableDeliveryDate(region, form.RentalWeeks);
        var deliveryDate = form.ParseDeliveryDate() ?? earliest;
        var pickupDate = deliveryDate.AddDays(RentalTerms.BaseRentalDays * PricingService.ClampWeeks(form.RentalWeeks));

        return new ScheduleViewModel
        {
            Form = form,
            Region = region,
            Package = package,
            Packages = packages,
            Zone = zone,
            Quote = package is null
                ? null
                : _pricing.Quote(package, zone, form.RentalWeeks, form.ToAddOnLines(), giftCredit),
            GiftCreditCents = giftCredit,
            GiftingAgentName = gift?.RealtorName,
            EarliestDeliveryDate = earliest,
            MinimumNoticeDays = region.Scheduling?.MinimumNoticeDays ?? 3,
            AvailableDeliveryWindows = SchedulingRules.GetAvailableWindows(
                region, deliveryDate, ScheduleOperations.Delivery),
            AvailablePickupWindows = SchedulingRules.GetAvailableWindows(
                region, pickupDate, ScheduleOperations.Pickup)
        };
    }

    private static DateOnly FindFirstBookableDeliveryDate(Region region, int rentalWeeks)
    {
        var date = SchedulingRules.EarliestDeliveryDate(region);
        var latest = DeliveryWindows.LatestDeliveryDate();
        var weeks = PricingService.ClampWeeks(rentalWeeks);
        while (date <= latest)
        {
            var pickupDate = date.AddDays(RentalTerms.BaseRentalDays * weeks);
            if (SchedulingRules.GetAvailableWindows(region, date, ScheduleOperations.Delivery).Count > 0
                && SchedulingRules.GetAvailableWindows(region, pickupDate, ScheduleOperations.Pickup).Count > 0)
            {
                return date;
            }

            date = date.AddDays(1);
        }

        return SchedulingRules.EarliestDeliveryDate(region);
    }
}
