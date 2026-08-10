using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Web.Models.Public;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

[Route("legal")]
public sealed class LegalController : Controller
{
    private readonly IRegionService _regions;

    public LegalController(IRegionService regions)
    {
        _regions = regions;
    }

    [HttpGet("rental-terms")]
    public async Task<IActionResult> RentalTerms(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Rental agreement";
        ViewData["Description"] = "The BoxDrop AZ moving tote rental agreement, including the rental period, extension pricing, damage and replacement fees, and cancellation policy.";

        var all = await _regions.GetActiveAsync(ct);
        var selected = string.IsNullOrWhiteSpace(region)
            ? await _regions.GetDefaultAsync(ct)
            : all.FirstOrDefault(r => string.Equals(r.Slug, region, StringComparison.OrdinalIgnoreCase))
              ?? await _regions.GetDefaultAsync(ct);

        return View(new RentalTermsViewModel
        {
            Region = selected,
            AllRegions = all,
            DamageFees = selected?.DamageFees ?? new DamageFeeSchedule()
        });
    }

    [HttpGet("privacy")]
    public IActionResult Privacy()
    {
        ViewData["Title"] = "Privacy policy";
        ViewData["Description"] = "How BoxDrop AZ collects, uses, and protects your personal information.";
        return View();
    }

    [HttpGet("terms")]
    public IActionResult Terms()
    {
        ViewData["Title"] = "Terms of Service";
        ViewData["Description"] = "Terms of Service for the BoxDrop AZ website and online services operated by Mastador Ventures, LLC.";
        return View();
    }
}
