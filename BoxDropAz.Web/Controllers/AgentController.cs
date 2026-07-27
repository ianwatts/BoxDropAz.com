using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Agent;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// The signed-in agent experience: subscription, credit balance, and sending closing gifts.
/// </summary>
[Authorize(Roles = Roles.Realtor + "," + Roles.RegionalAdmin + "," + Roles.SaaSAdmin)]
[Route("agent")]
public sealed class AgentController : Controller
{
    private readonly ISubscriptionService _subscriptions;
    private readonly IGiftService _gifts;
    private readonly IStripeGateway _stripe;
    private readonly IRegionService _regions;
    private readonly ICatalogService _catalog;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly GiftNotifier _notifier;
    private readonly IConfiguration _config;
    private readonly ILogger<AgentController> _logger;

    public AgentController(
        ISubscriptionService subscriptions,
        IGiftService gifts,
        IStripeGateway stripe,
        IRegionService regions,
        ICatalogService catalog,
        UserManager<ApplicationUser> userManager,
        GiftNotifier notifier,
        IConfiguration config,
        ILogger<AgentController> logger)
    {
        _subscriptions = subscriptions;
        _gifts = gifts;
        _stripe = stripe;
        _regions = regions;
        _catalog = catalog;
        _userManager = userManager;
        _notifier = notifier;
        _config = config;
        _logger = logger;
    }

    [HttpGet("")]
    public IActionResult Index() => RedirectToAction(nameof(Dashboard));

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        ViewData["Title"] = "Agent dashboard";

        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);
        var gifts = await _gifts.GetForRealtorAsync(user.Id, ct);

        var outstanding = gifts.Where(g => g.Status == GiftStatus.Sent).ToList();

        return View(new AgentDashboardViewModel
        {
            Subscription = subscription,
            Plan = RealtorPlan.FromId(subscription.PlanId),
            RecentGifts = gifts.OrderByDescending(g => g.CreatedAtUtc).Take(8).ToList(),
            Ledger = await _subscriptions.GetLedgerAsync(user.Id, 12, ct),
            GiftsClaimed = gifts.Count(g => g.Status == GiftStatus.Claimed),
            GiftsOutstanding = outstanding.Count,
            OutstandingValueCents = outstanding.Sum(g => g.GiftAmountCents),
            BillingPortalAvailable = _stripe.IsConfigured && !string.IsNullOrWhiteSpace(subscription.StripeCustomerId)
        });
    }

    // ---------- subscription ----------

    [HttpPost("subscribe")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Subscribe(RealtorPlanId planId, CancellationToken ct)
    {
        var plan = RealtorPlan.FromId(planId);
        if (plan is null)
        {
            return RedirectToAction("Plans", "Realtors");
        }

        var priceId = _stripe.GetPriceIdForPlan(planId);
        if (!_stripe.IsConfigured || string.IsNullOrWhiteSpace(priceId))
        {
            TempData["Error"] = "Subscriptions aren't available right now. Please contact us and we'll set you up.";
            return RedirectToAction("Plans", "Realtors");
        }

        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);

        if (subscription.IsActive && subscription.PlanId == planId)
        {
            TempData["Info"] = $"You're already on the {plan.Name} plan.";
            return RedirectToAction(nameof(Dashboard));
        }

        var customerId = await _stripe.EnsureCustomerAsync(user, ct);

        // Persisted before checkout so the webhook can find this agent by customer id even if the
        // invoice lands before the checkout session does.
        subscription.StripeCustomerId = customerId;
        await _subscriptions.SaveAsync(subscription, ct);

        var returnUrl = Url.Action(nameof(SubscribeReturn), "Agent", null, Request.Scheme)
                        + "?session_id={CHECKOUT_SESSION_ID}";

        var session = await _stripe.CreateSubscriptionSessionAsync(
            customerId,
            user.Id,
            priceId,
            returnUrl!,
            new Dictionary<string, string>
            {
                ["kind"] = CheckoutKind.RealtorSubscription,
                ["userId"] = user.Id,
                ["planId"] = planId.ToString(),
                ["regionId"] = user.RegionId ?? string.Empty
            },
            ct);

        ViewData["Title"] = $"Subscribe to {plan.Name}";

        return View("Subscribe", new SubscribeCheckoutViewModel
        {
            Plan = plan,
            ClientSecret = session.ClientSecret,
            PublishableKey = _config["Stripe:PublishableKey"] ?? string.Empty
        });
    }

    /// <summary>
    /// Where embedded Checkout drops the agent afterwards. The webhook does the real work; this
    /// just avoids showing a stale dashboard if the webhook is a second behind.
    /// </summary>
    [HttpGet("subscribe/return")]
    public async Task<IActionResult> SubscribeReturn(string? session_id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session_id))
        {
            return RedirectToAction(nameof(Dashboard));
        }

        var session = await _stripe.GetSessionAsync(session_id, ct);
        if (session?.Status != "complete")
        {
            TempData["Error"] = "Your subscription wasn't completed. Nothing was charged.";
            return RedirectToAction("Plans", "Realtors");
        }

        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);

        if (!subscription.IsActive)
        {
            // Optimistic: the webhook will overwrite this with Stripe's own view shortly.
            subscription.StripeCustomerId = session.CustomerId;
            subscription.StripeSubscriptionId = session.SubscriptionId;
            subscription.Status = "active";
            await _subscriptions.SaveAsync(subscription, ct);
        }

        TempData["Success"] = "You're subscribed. Your first credit lands as soon as Stripe confirms the payment.";
        return RedirectToAction(nameof(Dashboard));
    }

    [HttpGet("billing")]
    public async Task<IActionResult> Billing(CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetAsync(user.Id, ct);

        if (subscription?.StripeCustomerId is null)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        var returnUrl = Url.Action(nameof(Dashboard), "Agent", null, Request.Scheme)!;
        var portalUrl = await _stripe.CreateBillingPortalUrlAsync(subscription.StripeCustomerId, returnUrl, ct);

        if (portalUrl is null)
        {
            TempData["Error"] = "The billing portal isn't available right now. Email us and we'll sort it out.";
            return RedirectToAction(nameof(Dashboard));
        }

        return Redirect(portalUrl);
    }

    [HttpPost("cancel-subscription")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelSubscription(CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetAsync(user.Id, ct);

        if (subscription?.StripeSubscriptionId is null)
        {
            return RedirectToAction(nameof(Dashboard));
        }

        try
        {
            await _stripe.CancelSubscriptionAtPeriodEndAsync(subscription.StripeSubscriptionId, ct);
            TempData["Success"] =
                "Your plan will end at the close of the current billing period. Credit you've already earned stays usable.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not cancel subscription {SubscriptionId}", subscription.StripeSubscriptionId);
            TempData["Error"] = "We couldn't cancel that automatically. Email us and we'll take care of it.";
        }

        return RedirectToAction(nameof(Dashboard));
    }

    // ---------- gifts ----------

    [HttpGet("gifts")]
    public async Task<IActionResult> Gifts(string? status, CancellationToken ct)
    {
        ViewData["Title"] = "Gift history";

        var user = await RequireUserAsync();
        var gifts = await _gifts.GetForRealtorAsync(user.Id, ct);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<GiftStatus>(status, true, out var parsed))
        {
            gifts = gifts.Where(g => g.Status == parsed).ToList();
        }

        return View(new GiftListViewModel
        {
            Gifts = gifts.OrderByDescending(g => g.CreatedAtUtc).ToList(),
            Subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct),
            StatusFilter = status
        });
    }

    [HttpGet("gifts/new")]
    public async Task<IActionResult> SendGift(CancellationToken ct)
    {
        ViewData["Title"] = "Send a closing gift";

        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);

        if (!subscription.IsActive)
        {
            TempData["Info"] = "Choose a plan first and your gift credit starts immediately.";
            return RedirectToAction("Plans", "Realtors");
        }

        return View(await BuildSendGiftViewModelAsync(new GiftFormModel(), subscription, ct));
    }

    [HttpPost("gifts/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendGift(GiftFormModel form, CancellationToken ct)
    {
        ViewData["Title"] = "Send a closing gift";

        var user = await RequireUserAsync();
        var subscription = await _subscriptions.GetOrCreateAsync(user.Id, user.RegionId ?? string.Empty, ct);

        if (!subscription.IsActive)
        {
            return RedirectToAction("Plans", "Realtors");
        }

        if (!DateOnly.TryParse(form.ClosingDate, out var closingDate))
        {
            ModelState.AddModelError(nameof(form.ClosingDate), "Enter the closing date.");
        }
        else if (closingDate < DeliveryWindows.TodayInArizona().AddDays(-90))
        {
            ModelState.AddModelError(nameof(form.ClosingDate), "That closing date is too far in the past.");
        }

        if (form.GiftAmountCents > subscription.CreditBalanceCents)
        {
            ModelState.AddModelError(nameof(form.GiftAmountDollars),
                $"You have {Money.Format(subscription.CreditBalanceCents)} of credit available.");
        }

        var (region, _) = await _regions.ResolveZipAsync(form.PropertyZip, ct);
        if (region is null)
        {
            ModelState.AddModelError(nameof(form.PropertyZip),
                $"We don't deliver to {form.PropertyZip} yet, so your client couldn't redeem this gift.");
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildSendGiftViewModelAsync(form, subscription, ct));
        }

        // Debit first. The conditional update is what stops two tabs spending the same balance.
        var (deducted, newBalance) = await _subscriptions.TryDeductCreditAsync(user.Id, form.GiftAmountCents, ct);
        if (!deducted)
        {
            ModelState.AddModelError(nameof(form.GiftAmountDollars),
                $"Your balance changed while you were filling this in. You now have {Money.Format(newBalance)} available.");
            return View(await BuildSendGiftViewModelAsync(form, subscription, ct));
        }

        var gift = new GiftOrder
        {
            GiftId = Guid.NewGuid().ToString("N"),
            ClaimToken = _gifts.GenerateClaimToken(),
            RealtorUserId = user.Id,
            RealtorName = user.FullName ?? user.Email ?? "Your agent",
            RealtorCompany = user.CompanyName,
            RealtorEmail = user.Email ?? string.Empty,
            RealtorPhone = user.PhoneNumber,
            RegionId = region!.Id,
            ClientName = form.ClientName,
            ClientEmail = form.ClientEmail,
            ClientPhone = form.ClientPhone,
            PropertyAddressLine1 = form.PropertyAddressLine1,
            PropertyCity = form.PropertyCity,
            PropertyZip = form.PropertyZip,
            ClosingDate = form.ClosingDate,
            GiftAmountCents = form.GiftAmountCents,
            PersonalMessage = form.PersonalMessage,
            IncludeCoBrandingInsert = form.IncludeCoBrandingInsert && subscription.CoBrandingEnabled,
            Status = GiftStatus.Sent
        };

        try
        {
            await _gifts.SaveAsync(gift, ct);
        }
        catch (Exception ex)
        {
            // Put the money back rather than leaving the agent short for a gift that never existed.
            _logger.LogError(ex, "Could not save gift for realtor {UserId}; refunding credit", user.Id);
            await _subscriptions.RefundCreditAsync(user.Id, form.GiftAmountCents, gift.GiftId,
                "Refund: the gift could not be created", ct);

            TempData["Error"] = "Something went wrong sending that gift. Your credit wasn't touched. Please try again.";
            return RedirectToAction(nameof(SendGift));
        }

        await _subscriptions.WriteLedgerEntryAsync(new CreditLedgerEntry
        {
            UserId = user.Id,
            EntryId = CreditLedgerEntry.NewEntryId(DateTime.UtcNow),
            Kind = CreditEntryKind.Debit,
            AmountCents = -form.GiftAmountCents,
            BalanceAfterCents = newBalance,
            Description = $"Gift to {gift.ClientName}",
            RelatedGiftId = gift.GiftId
        }, ct);

        await _notifier.SendClaimLinkAsync(gift, ct);
        await _notifier.SendAgentReceiptAsync(gift, newBalance, ct);

        TempData["Success"] =
            $"{Money.Format(gift.GiftAmountCents)} sent to {gift.ClientName}. They have a claim link in their inbox.";

        return RedirectToAction(nameof(Gifts));
    }

    [HttpPost("gifts/{giftId}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelGift(string giftId, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var gift = await _gifts.GetAsync(giftId, ct);

        if (gift is null || gift.RealtorUserId != user.Id)
        {
            return NotFound();
        }

        if (gift.Status != GiftStatus.Sent)
        {
            TempData["Error"] = "That gift has already been claimed or cancelled.";
            return RedirectToAction(nameof(Gifts));
        }

        gift.Status = GiftStatus.Cancelled;
        gift.CancelledAtUtc = DateTime.UtcNow;
        await _gifts.SaveAsync(gift, ct);

        var balance = await _subscriptions.RefundCreditAsync(
            user.Id, gift.GiftAmountCents, gift.GiftId, $"Cancelled gift to {gift.ClientName}", ct);

        TempData["Success"] =
            $"Gift cancelled. {Money.Format(gift.GiftAmountCents)} is back in your balance ({Money.Format(balance)} available).";

        return RedirectToAction(nameof(Gifts));
    }

    [HttpPost("gifts/{giftId}/resend")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendGift(string giftId, CancellationToken ct)
    {
        var user = await RequireUserAsync();
        var gift = await _gifts.GetAsync(giftId, ct);

        if (gift is null || gift.RealtorUserId != user.Id)
        {
            return NotFound();
        }

        if (!gift.IsClaimable)
        {
            TempData["Error"] = "That gift can no longer be claimed, so there's nothing to resend.";
            return RedirectToAction(nameof(Gifts));
        }

        await _notifier.SendClaimLinkAsync(gift, ct);
        TempData["Success"] = $"Claim link resent to {gift.ClientEmail}.";

        return RedirectToAction(nameof(Gifts));
    }

    // ---------- helpers ----------

    private async Task<ApplicationUser> RequireUserAsync()
        => await _userManager.GetUserAsync(User)
           ?? throw new InvalidOperationException("Signed in but the user record is missing.");

    private async Task<SendGiftViewModel> BuildSendGiftViewModelAsync(
        GiftFormModel form,
        RealtorSubscription subscription,
        CancellationToken ct)
    {
        var region = await _regions.GetByIdAsync(subscription.RegionId, ct) ?? await _regions.GetDefaultAsync(ct);
        var packages = region is null
            ? new List<Core.Models.Catalog.CratePackage>()
            : await _catalog.GetPackagesAsync(region.Id, ct);

        // Anchoring each amount to a real bundle makes the choice concrete instead of arbitrary.
        var suggestions = packages
            .OrderBy(p => p.BasePriceCents)
            .Select(p => new GiftSuggestion(
                p.BasePriceCents,
                Money.FormatCompact(p.BasePriceCents),
                $"Covers the {p.Name} bundle in full",
                p.BasePriceCents <= subscription.CreditBalanceCents))
            .ToList();

        return new SendGiftViewModel
        {
            Form = form,
            Subscription = subscription,
            Plan = RealtorPlan.FromId(subscription.PlanId),
            Packages = packages,
            Region = region,
            Suggestions = suggestions
        };
    }
}
