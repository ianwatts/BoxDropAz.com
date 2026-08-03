using System.Security.Claims;
using System.Text;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Account;
using BoxDropAz.Web.Models.Identity;
using BoxDropAz.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxDropAz.Web.Controllers;

public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailSender<ApplicationUser> _emailSender;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailSender<ApplicationUser> emailSender,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Login(string? returnUrl = null, string? message = null)
    {
        ViewData["Title"] = "Sign in";
        ViewData["Message"] = message;
        await PopulateExternalProvidersAsync();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Title"] = "Sign in";
        await PopulateExternalProvidersAsync();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "That email and password combination is not recognized.");
            return View(model);
        }

        if (user.IsDisabled)
        {
            ModelState.AddModelError(string.Empty, "This account has been disabled. Contact support for help.");
            return View(model);
        }

        if (!user.EmailConfirmed)
        {
            ModelState.AddModelError(string.Empty, "Please confirm your email address before signing in.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "That email and password combination is not recognized.");
            return View(model);
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        return await RedirectAfterSignInAsync(user, model.ReturnUrl);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null, string? accountType = null)
    {
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl, accountType });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
        properties.Items["accountType"] = NormalizeAccountType(accountType);
        return Challenge(properties, provider);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLoginCallback(
        string? returnUrl = null,
        string? accountType = null,
        string? remoteError = null)
    {
        if (!string.IsNullOrWhiteSpace(remoteError))
        {
            _logger.LogWarning("External login provider returned an error: {Error}", remoteError);
            return RedirectToAction(nameof(Login), new { returnUrl, message = "ExternalLoginFailed" });
        }

        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl, message = "ExternalLoginFailed" });
        }

        var existingSignIn = await _signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: true,
            bypassTwoFactor: true);

        if (existingSignIn.Succeeded)
        {
            var signedInUser = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (signedInUser is null || signedInUser.IsDisabled)
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction(nameof(Login), new { returnUrl, message = "AccountDisabled" });
            }

            signedInUser.LastLoginAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(signedInUser);
            return await RedirectAfterSignInAsync(signedInUser, returnUrl);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email)
                    ?? info.Principal.FindFirstValue("email");
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToAction(nameof(Login), new { returnUrl, message = "ExternalLoginEmailRequired" });
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is not null)
        {
            if (user.IsDisabled)
            {
                return RedirectToAction(nameof(Login), new { returnUrl, message = "AccountDisabled" });
            }

            var linkResult = await _userManager.AddLoginAsync(user, info);
            if (!linkResult.Succeeded)
            {
                _logger.LogWarning(
                    "Failed to link {Provider} login to existing user {Email}: {Errors}",
                    info.LoginProvider,
                    email,
                    string.Join("; ", linkResult.Errors.Select(e => e.Description)));
                return RedirectToAction(nameof(Login), new { returnUrl, message = "ExternalLoginFailed" });
            }

            if (!user.EmailConfirmed)
            {
                user.EmailConfirmed = true;
            }

            if (string.IsNullOrWhiteSpace(user.FullName))
            {
                user.FullName = ResolveFullName(info.Principal, email);
            }

            user.LastLoginAtUtc = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await _signInManager.SignInAsync(user, isPersistent: true);
            return await RedirectAfterSignInAsync(user, returnUrl);
        }

        var roleSource = accountType;
        if (info.AuthenticationProperties?.Items is { } items
            && items.TryGetValue("accountType", out var itemAccountType)
            && !string.IsNullOrWhiteSpace(itemAccountType))
        {
            roleSource = itemAccountType;
        }

        var role = NormalizeAccountType(roleSource);

        user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = ResolveFullName(info.Principal, email),
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            _logger.LogWarning(
                "Failed to create user from {Provider} login for {Email}: {Errors}",
                info.LoginProvider,
                email,
                string.Join("; ", createResult.Errors.Select(e => e.Description)));
            return RedirectToAction(nameof(Login), new { returnUrl, message = "ExternalLoginFailed" });
        }

        await _userManager.AddToRoleAsync(user, role);
        await _userManager.AddLoginAsync(user, info);

        user.LastLoginAtUtc = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);
        await _signInManager.SignInAsync(user, isPersistent: true);

        _logger.LogInformation(
            "Registered new {AccountType} via {Provider}: {Email}",
            role,
            info.LoginProvider,
            email);

        return await RedirectAfterSignInAsync(user, returnUrl);
    }

    [HttpPost]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Register(string? returnUrl = null, string? accountType = null)
    {
        ViewData["Title"] = "Create your account";
        await PopulateExternalProvidersAsync();
        return View(new RegisterViewModel
        {
            ReturnUrl = returnUrl,
            AccountType = NormalizeAccountType(accountType)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        ViewData["Title"] = "Create your account";
        model.AccountType = NormalizeAccountType(model.AccountType);
        await PopulateExternalProvidersAsync();

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _userManager.FindByEmailAsync(model.Email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "An account with that email already exists.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.Phone,
            CompanyName = model.CompanyName,
            EmailConfirmed = false,
            SecurityStamp = Guid.NewGuid().ToString()
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, model.AccountType);

        var confirmationLink = await BuildConfirmationLinkAsync(user, model.ReturnUrl);
        await _emailSender.SendConfirmationLinkAsync(user, model.Email, confirmationLink);

        _logger.LogInformation("Registered new {AccountType}: {Email}", model.AccountType, model.Email);

        return RedirectToAction(nameof(RegisterConfirmation), new { email = model.Email });
    }

    [HttpGet]
    public IActionResult RegisterConfirmation(string? email)
    {
        ViewData["Title"] = "Check your email";
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> ConfirmEmail(string? userId, string? code, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(code))
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidConfirmation" });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidConfirmation" });
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidConfirmation" });
        }

        var result = await _userManager.ConfirmEmailAsync(user, decoded);
        if (!result.Succeeded)
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidConfirmation" });
        }

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(Login), new { message = "EmailConfirmed" });
    }

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewData["Title"] = "Reset your password";
        return View(new ForgotPasswordViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        ViewData["Title"] = "Reset your password";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);

        // Always report success so this endpoint cannot be used to enumerate accounts.
        if (user is not null && user.EmailConfirmed)
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
            var link = Url.Action(nameof(ResetPassword), "Account",
                new { email = model.Email, code }, Request.Scheme)!;
            await _emailSender.SendPasswordResetLinkAsync(user, model.Email, link);
        }

        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        ViewData["Title"] = "Check your email";
        return View();
    }

    [HttpGet]
    public IActionResult ResetPassword(string? email, string? code)
    {
        ViewData["Title"] = "Choose a new password";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidReset" });
        }

        return View(new ResetPasswordViewModel { Email = email, Code = code });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        ViewData["Title"] = "Choose a new password";

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { message = "PasswordReset" });
        }

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(model.Code));
        }
        catch (FormatException)
        {
            return RedirectToAction(nameof(Login), new { message = "InvalidReset" });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        return RedirectToAction(nameof(Login), new { message = "PasswordReset" });
    }

    [HttpGet]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access denied";
        return View();
    }

    private async Task PopulateExternalProvidersAsync()
    {
        var providers = await _signInManager.GetExternalAuthenticationSchemesAsync();
        ViewData["ExternalLogins"] = providers.ToList();
    }

    private async Task<IActionResult> RedirectAfterSignInAsync(ApplicationUser user, string? returnUrl)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Redirect(RoleHome.ForRoles(roles));
    }

    private static string ResolveFullName(ClaimsPrincipal principal, string email)
    {
        var name = principal.FindFirstValue(ClaimTypes.Name)
                   ?? principal.FindFirstValue("name");
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        var given = principal.FindFirstValue(ClaimTypes.GivenName);
        var family = principal.FindFirstValue(ClaimTypes.Surname);
        var combined = $"{given} {family}".Trim();
        if (!string.IsNullOrWhiteSpace(combined))
        {
            return combined;
        }

        return email.Split('@')[0];
    }

    private async Task<string> BuildConfirmationLinkAsync(ApplicationUser user, string? returnUrl)
    {
        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        return Url.Action(nameof(ConfirmEmail), "Account",
            new { userId = user.Id, code, returnUrl }, Request.Scheme)!;
    }

    /// <summary>Only two roles are self-serve; everything else is created by an admin.</summary>
    private static string NormalizeAccountType(string? accountType)
        => string.Equals(accountType, Roles.Realtor, StringComparison.OrdinalIgnoreCase)
            ? Roles.Realtor
            : Roles.Customer;
}
