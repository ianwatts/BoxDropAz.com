using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Web.Models.Gift;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// The client's side of a realtor gift. The landing page sells the gift; the actual booking hands
/// off to the normal wizard with the claim token attached, so there is only one checkout path.
/// </summary>
[AllowAnonymous]
[Route("gift")]
public sealed class GiftController : Controller
{
    private readonly IGiftService _gifts;
    private readonly IRegionService _regions;
    private readonly ICatalogService _catalog;

    public GiftController(IGiftService gifts, IRegionService regions, ICatalogService catalog)
    {
        _gifts = gifts;
        _regions = regions;
        _catalog = catalog;
    }

    [HttpGet("claim/{token}")]
    public async Task<IActionResult> Claim(string token, CancellationToken ct)
    {
        var gift = await _gifts.GetByClaimTokenAsync(token, ct);
        if (gift is null)
        {
            return View("Unavailable", new GiftUnavailableViewModel
            {
                Reason = "We couldn't find that gift. The link may have been mistyped or truncated by an email client."
            });
        }

        if (gift.Status == GiftStatus.Claimed)
        {
            return View("Unavailable", new GiftUnavailableViewModel
            {
                Reason = "This gift has already been claimed. If that was you, sign in to see your rental.",
                AgentName = gift.RealtorName
            });
        }

        if (gift.Status == GiftStatus.Cancelled)
        {
            return View("Unavailable", new GiftUnavailableViewModel
            {
                Reason = "This gift was cancelled. Reach out to your agent if you think that's a mistake.",
                AgentName = gift.RealtorName
            });
        }

        if (!gift.IsClaimable)
        {
            return View("Unavailable", new GiftUnavailableViewModel
            {
                Reason = "This gift expired. Your agent can send a new one in a couple of clicks.",
                AgentName = gift.RealtorName
            });
        }

        ViewData["Title"] = $"{gift.RealtorName} sent you a closing gift";

        var region = await _regions.GetByIdAsync(gift.RegionId, ct) ?? await _regions.GetDefaultAsync(ct);
        var packages = region is null
            ? new List<Core.Models.Catalog.CratePackage>()
            : await _catalog.GetPackagesAsync(region.Id, ct);

        return View(new GiftClaimViewModel
        {
            Gift = gift,
            Region = region,
            Packages = packages,
            FullyCoveredPackage = packages
                .Where(p => p.BasePriceCents <= gift.GiftAmountCents)
                .OrderByDescending(p => p.BasePriceCents)
                .FirstOrDefault()
        });
    }
}
