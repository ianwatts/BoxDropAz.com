using System.Diagnostics;
using System.Net;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Web.Models;
using BoxDropAz.Web.Models.Public;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly IRegionService _regions;
    private readonly ICatalogService _catalog;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IRegionService regions,
        ICatalogService catalog,
        IEmailService email,
        IConfiguration config,
        ILogger<HomeController> logger)
    {
        _regions = regions;
        _catalog = catalog;
        _email = email;
        _config = config;
        _logger = logger;
    }

    public async Task<IActionResult> Index(string? region, CancellationToken ct)
    {
        var (selected, all) = await ResolveRegionAsync(region, ct);

        return View(new LandingViewModel
        {
            Region = selected,
            AllRegions = all,
            Packages = selected is null ? new List<CratePackage>() : await _catalog.GetPackagesAsync(selected.Id, ct)
        });
    }

    public IActionResult HowItWorks()
    {
        ViewData["Title"] = "How it works";
        ViewData["Description"] = "Book online, we drop off stackable crates and dollies, you pack at your own pace, then we pick everything up. No cardboard, no tape, no cleanup.";
        return View();
    }

    public async Task<IActionResult> Pricing(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Pricing";
        ViewData["Description"] = "Flat rate crate rental bundles from $89 for a one week rental, including free delivery and pickup in our core service zone.";

        var (selected, all) = await ResolveRegionAsync(region, ct);

        return View(new PricingViewModel
        {
            Region = selected,
            AllRegions = all,
            Packages = selected is null ? new List<CratePackage>() : await _catalog.GetPackagesAsync(selected.Id, ct),
            AddOns = AddOnCatalog.All,
            DamageFees = selected?.DamageFees ?? new DamageFeeSchedule()
        });
    }

    public async Task<IActionResult> ServiceAreas(string? zip, CancellationToken ct)
    {
        ViewData["Title"] = "Service areas";
        ViewData["Description"] = "Check whether BoxDrop AZ delivers to your ZIP code across the Phoenix East Valley, Pinal County, and Tucson.";

        var all = await _regions.GetActiveAsync(ct);
        var model = new ServiceAreasViewModel
        {
            AllRegions = all,
            CheckedZip = zip?.Trim()
        };

        if (!string.IsNullOrWhiteSpace(model.CheckedZip))
        {
            var (matchedRegion, matchedZone) = await _regions.ResolveZipAsync(model.CheckedZip, ct);
            model.MatchedRegion = matchedRegion;
            model.MatchedZone = matchedZone;
        }

        return View(model);
    }

    public IActionResult Faq()
    {
        ViewData["Title"] = "Frequently asked questions";
        ViewData["Description"] = "Answers about crate sizes, rental length, delivery windows, damage fees, and how to extend a rental.";
        return View();
    }

    [HttpGet]
    public IActionResult Contact()
    {
        ViewData["Title"] = "Contact us";
        return View(new ContactViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactViewModel model, CancellationToken ct)
    {
        ViewData["Title"] = "Contact us";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var adminEmail = _config["Site:AdminEmail"] ?? _config["Site:SupportEmail"];
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            var body = EmailTemplates.Wrap(
                "New website enquiry",
                EmailTemplates.DetailRows(
                    ("Name", model.Name),
                    ("Email", model.Email),
                    ("Phone", model.Phone ?? "-"))
                + $"<p style=\"white-space:pre-wrap;\">{WebUtility.HtmlEncode(model.Message)}</p>");

            await _email.SendAsync(adminEmail, $"Website enquiry from {model.Name}", body, ct);
        }
        else
        {
            _logger.LogInformation("Contact form submitted by {Email} but no admin address is configured", model.Email);
        }

        TempData["Success"] = "Thanks for reaching out. We'll get back to you within one business day.";
        return RedirectToAction(nameof(Contact));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["Title"] = "Something went wrong";
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private async Task<(Region? Selected, List<Region> All)> ResolveRegionAsync(string? regionSlug, CancellationToken ct)
    {
        var all = await _regions.GetActiveAsync(ct);

        var selected = string.IsNullOrWhiteSpace(regionSlug)
            ? await _regions.GetDefaultAsync(ct)
            : all.FirstOrDefault(r => string.Equals(r.Slug, regionSlug, StringComparison.OrdinalIgnoreCase))
              ?? await _regions.GetDefaultAsync(ct);

        return (selected, all);
    }
}
