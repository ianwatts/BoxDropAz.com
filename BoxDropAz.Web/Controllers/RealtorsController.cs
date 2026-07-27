using BoxDropAz.Core.Models.Realtors;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// Public facing marketing for the agent gifting program. The signed-in agent experience lives in
/// <see cref="AgentController"/>.
/// </summary>
public sealed class RealtorsController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Closing gifts for real estate agents";
        ViewData["Description"] = "Gift reusable moving crates to your closing clients. Monthly credit, co-branded delivery, and a claim link your client redeems on their own schedule.";
        return View();
    }

    public IActionResult Plans()
    {
        ViewData["Title"] = "Agent plans and pricing";
        ViewData["Description"] = "Starter, Professional, and Brokerage plans for real estate agents gifting reusable moving crates at closing. Credit rolls over up to three months.";
        return View(RealtorPlan.All);
    }
}
