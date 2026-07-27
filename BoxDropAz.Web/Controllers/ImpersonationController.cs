using System.Security.Claims;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BoxDropAz.Web.Controllers;

/// <summary>
/// Lets an admin sign in as another user to reproduce what they are seeing. The original identity is
/// stashed in claims on the impersonated cookie, which is what makes stopping possible without a
/// second login and what drives the warning banner.
/// </summary>
[Authorize]
public sealed class ImpersonationController : Controller
{
    public const string IsImpersonatingClaim = "IsImpersonating";
    public const string OriginalUserIdClaim = "OriginalUserId";
    public const string OriginalUserNameClaim = "OriginalUserName";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<ImpersonationController> _logger;

    public ImpersonationController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<ImpersonationController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "AnyAdmin")]
    public async Task<IActionResult> Start(string userId, string? returnUrl)
    {
        // Nesting impersonation would lose the real original identity, so refuse it.
        if (User.HasClaim(IsImpersonatingClaim, "true"))
        {
            TempData["Error"] = "Stop the current impersonation session before starting another.";
            return RedirectToLocal(returnUrl);
        }

        var admin = await _userManager.GetUserAsync(User);
        var target = await _userManager.FindByIdAsync(userId);

        if (admin is null || target is null)
        {
            TempData["Error"] = "That account no longer exists.";
            return RedirectToLocal(returnUrl);
        }

        if (target.Id == admin.Id)
        {
            return RedirectToLocal(returnUrl);
        }

        if (!await CanImpersonateAsync(admin, target))
        {
            TempData["Error"] = "You can't impersonate that account.";
            return RedirectToLocal(returnUrl);
        }

        _logger.LogWarning(
            "{Admin} ({AdminId}) started impersonating {Target} ({TargetId})",
            admin.Email, admin.Id, target.Email, target.Id);

        // Not persistent: closing the browser should end an impersonation session.
        await _signInManager.SignOutAsync();
        await _signInManager.SignInWithClaimsAsync(target, isPersistent: false, new[]
        {
            new Claim(IsImpersonatingClaim, "true"),
            new Claim(OriginalUserIdClaim, admin.Id),
            new Claim(OriginalUserNameClaim, admin.Email ?? admin.DisplayName)
        });

        return Redirect(RoleHome.ForRoles(await _userManager.GetRolesAsync(target)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Stop()
    {
        var originalUserId = User.FindFirstValue(OriginalUserIdClaim);
        if (string.IsNullOrWhiteSpace(originalUserId))
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction("Login", "Account");
        }

        var admin = await _userManager.FindByIdAsync(originalUserId);
        await _signInManager.SignOutAsync();

        if (admin is null || admin.IsDisabled)
        {
            // The admin account went away mid-session; a fresh login is the only safe outcome.
            return RedirectToAction("Login", "Account");
        }

        await _signInManager.SignInAsync(admin, isPersistent: false);

        _logger.LogInformation("{Admin} stopped impersonating", admin.Email);
        TempData["Success"] = "You're back to your own account.";

        return Redirect(RoleHome.ForRoles(await _userManager.GetRolesAsync(admin)));
    }

    /// <summary>
    /// A regional admin is confined to their own region and cannot reach into a higher role; only a
    /// platform admin has no fence.
    /// </summary>
    private async Task<bool> CanImpersonateAsync(ApplicationUser admin, ApplicationUser target)
    {
        if (User.IsInRole(Roles.SaaSAdmin))
        {
            return true;
        }

        var targetRoles = await _userManager.GetRolesAsync(target);
        if (targetRoles.Contains(Roles.SaaSAdmin) || targetRoles.Contains(Roles.RegionalAdmin))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(admin.RegionId) && target.RegionId == admin.RegionId;
    }

    private IActionResult RedirectToLocal(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? Redirect(returnUrl)
            : RedirectToAction("Index", "Admin");
}
