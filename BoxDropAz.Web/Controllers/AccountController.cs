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
    public IActionResult Login(string? returnUrl = null, string? message = null)
    {
        ViewData["Title"] = "Sign in";
        ViewData["Message"] = message;
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        ViewData["Title"] = "Sign in";

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

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return Redirect(model.ReturnUrl);
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Redirect(RoleHome.ForRoles(roles));
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
    public IActionResult Register(string? returnUrl = null, string? accountType = null)
    {
        ViewData["Title"] = "Create your account";
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
            EmailConfirmed = false
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
