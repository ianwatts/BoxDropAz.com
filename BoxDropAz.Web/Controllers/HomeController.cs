using System.Diagnostics;
using System.Net;
using System.Text;
using System.Xml.Linq;
using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Orders;
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
    private readonly IOrderService _orders;
    private readonly StaffNotifier _staff;
    private readonly IConfiguration _config;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IRegionService regions,
        ICatalogService catalog,
        IOrderService orders,
        StaffNotifier staff,
        IConfiguration config,
        ILogger<HomeController> logger)
    {
        _regions = regions;
        _catalog = catalog;
        _orders = orders;
        _staff = staff;
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
        ViewData["Description"] = "Book online, we drop off 27-gallon totes with lids and custom-fit dollies, you pack at your own pace, then we pick everything up.";
        return View();
    }

    public async Task<IActionResult> Pricing(string? region, CancellationToken ct)
    {
        ViewData["Title"] = "Pricing";
        ViewData["Description"] = "Flat rate moving tote rental bundles from $89 for one week, including lids, dollies, delivery, and pickup in our core service zone.";

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

    [HttpGet("/thank-you")]
    public async Task<IActionResult> ThankYou(string? package, string? orderId, bool accountCreated, CancellationToken ct)
    {
        ViewData["Title"] = "Thank you";
        ViewData["Description"] = "Your BoxDrop AZ tote rental is booked.";

        if (string.IsNullOrWhiteSpace(orderId))
        {
            return View(new ThankYouViewModel());
        }

        var order = await _orders.GetAsync(orderId, ct);
        if (order is null || order.Status == OrderStatus.Cancelled)
        {
            return View(new ThankYouViewModel());
        }

        if (order.Status == OrderStatus.PendingPayment)
        {
            return RedirectToAction("Complete", "Booking", new { orderId, accountCreated });
        }

        if (!string.Equals(package, order.PackageId, StringComparison.OrdinalIgnoreCase))
        {
            return RedirectToAction(nameof(ThankYou), new
            {
                package = order.PackageId,
                orderId = order.OrderId,
                accountCreated = accountCreated ? true : (bool?)null
            });
        }

        return View(new ThankYouViewModel
        {
            Order = order,
            AccountCreated = accountCreated
        });
    }

    public IActionResult Faq()
    {
        ViewData["Title"] = "Frequently asked questions";
        ViewData["Description"] = "Answers about tote sizes, lids, rental length, delivery windows, damage fees, and how to extend a rental.";
        return View();
    }

    [HttpGet("/robots.txt")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Robots()
    {
        var configuredBaseUrl = _config["Site:BaseUrl"]?.TrimEnd('/');
        var productionHost = Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var productionUri)
            ? productionUri.Host
            : null;
        var isProductionHost = !string.IsNullOrWhiteSpace(productionHost)
            && string.Equals(Request.Host.Host, productionHost, StringComparison.OrdinalIgnoreCase);

        var body = isProductionHost
            ? $"User-agent: *\nAllow: /\nDisallow: /Account/\nDisallow: /Admin/\nDisallow: /Agent/\nDisallow: /Booking/\nDisallow: /Dashboard/\nDisallow: /Gift/\nDisallow: /SaaSAdmin/\nDisallow: /Worker/\n\nSitemap: {configuredBaseUrl}/sitemap.xml\n"
            : "User-agent: *\nDisallow: /\n";

        return Content(body, "text/plain", Encoding.UTF8);
    }

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public IActionResult Sitemap()
    {
        var configuredBaseUrl = _config["Site:BaseUrl"]?.TrimEnd('/');
        var baseUrl = string.IsNullOrWhiteSpace(configuredBaseUrl)
            ? $"{Request.Scheme}://{Request.Host}"
            : configuredBaseUrl;

        var pages = new (string Path, string ChangeFrequency, decimal Priority)[]
        {
            ("/", "weekly", 1.0m),
            ("/Home/HowItWorks", "monthly", 0.8m),
            ("/Home/Pricing", "weekly", 0.9m),
            ("/Home/ServiceAreas", "weekly", 0.9m),
            ("/Home/Faq", "monthly", 0.7m),
            ("/Home/Contact", "yearly", 0.5m),
            ("/Realtors", "monthly", 0.7m),
            ("/Realtors/Plans", "monthly", 0.7m),
            ("/legal/terms", "yearly", 0.3m),
            ("/legal/rental-terms", "yearly", 0.3m),
            ("/legal/privacy", "yearly", 0.3m)
        };

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var document = new XDocument(
            new XElement(ns + "urlset",
                pages.Select(page =>
                    new XElement(ns + "url",
                        new XElement(ns + "loc", $"{baseUrl}{page.Path}"),
                        new XElement(ns + "changefreq", page.ChangeFrequency),
                        new XElement(ns + "priority", page.Priority.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))))));

        return Content(document.ToString(), "application/xml", Encoding.UTF8);
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

        var body = EmailTemplates.Wrap(
            "New website enquiry",
            EmailTemplates.DetailRows(
                ("Name", model.Name),
                ("Email", model.Email),
                ("Phone", model.Phone ?? "-"))
            + $"<p style=\"white-space:pre-wrap;\">{WebUtility.HtmlEncode(model.Message)}</p>");

        await _staff.NotifyGlobalAsync(
            NotificationTypes.ContactForm,
            $"Website enquiry from {model.Name}",
            body,
            ct);

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
