using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Fans staff emails out to roles and/or specific users configured under Admin → Notifications.
/// Falls back to Site:AdminEmail when nothing is configured for a type.
/// </summary>
public sealed class StaffNotifier
{
    private readonly IEmailService _email;
    private readonly IRegionService _regions;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _config;
    private readonly ILogger<StaffNotifier> _logger;

    public StaffNotifier(
        IEmailService email,
        IRegionService regions,
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger<StaffNotifier> logger)
    {
        _email = email;
        _regions = regions;
        _userManager = userManager;
        _config = config;
        _logger = logger;
    }

    public Task NotifyRegionAsync(
        string? regionId,
        string notificationType,
        string subject,
        string htmlBody,
        CancellationToken ct = default)
        => SendAsync(regionId, notificationType, subject, htmlBody, ct);

    /// <summary>
    /// Site-wide alerts (e.g. contact form) — union recipients across every active region.
    /// </summary>
    public async Task NotifyGlobalAsync(
        string notificationType,
        string subject,
        string htmlBody,
        CancellationToken ct = default)
    {
        var regions = await _regions.GetActiveAsync(ct);
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var region in regions)
        {
            foreach (var email in await ResolveRecipientsAsync(region, notificationType, ct))
            {
                recipients.Add(email);
            }
        }

        if (recipients.Count == 0)
        {
            AddFallback(recipients);
        }

        await SendToAsync(recipients, subject, htmlBody, notificationType, ct);
    }

    private async Task SendAsync(
        string? regionId,
        string notificationType,
        string subject,
        string htmlBody,
        CancellationToken ct)
    {
        Region? region = null;
        if (!string.IsNullOrWhiteSpace(regionId))
        {
            region = await _regions.GetByIdAsync(regionId, ct);
        }

        var recipients = new HashSet<string>(
            await ResolveRecipientsAsync(region, notificationType, ct),
            StringComparer.OrdinalIgnoreCase);
        if (recipients.Count == 0)
        {
            AddFallback(recipients);
        }

        await SendToAsync(recipients, subject, htmlBody, notificationType, ct);
    }

    private async Task SendToAsync(
        IReadOnlyCollection<string> recipients,
        string subject,
        string htmlBody,
        string notificationType,
        CancellationToken ct)
    {
        if (recipients.Count == 0)
        {
            _logger.LogWarning(
                "No staff recipients for {NotificationType}; email skipped",
                notificationType);
            return;
        }

        foreach (var to in recipients)
        {
            await _email.SendAsync(to, subject, htmlBody, ct);
        }
    }

    private void AddFallback(HashSet<string> recipients)
    {
        var fallback = _config["Site:AdminEmail"] ?? _config["Site:SupportEmail"];
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            recipients.Add(fallback.Trim());
        }
    }

    public async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        Region? region,
        string notificationType,
        CancellationToken ct = default)
    {
        var settings = region?.Notifications;
        if (settings is null || settings.Subscriptions.Count == 0)
        {
            settings = RegionNotificationSettings.CreateDefaults();
        }

        var sub = settings.For(notificationType);
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (sub.NotifySaaSAdmin)
        {
            await AddRoleEmailsAsync(Roles.SaaSAdmin, regionId: null, emails, ct);
        }

        if (sub.NotifyRegionalAdmin)
        {
            await AddRoleEmailsAsync(Roles.RegionalAdmin, region?.Id, emails, ct);
        }

        if (sub.NotifyWorker)
        {
            await AddRoleEmailsAsync(Roles.Worker, region?.Id, emails, ct);
        }

        foreach (var userId in sub.ExtraUserIds.Where(id => !string.IsNullOrWhiteSpace(id)))
        {
            var user = await _userManager.FindByIdAsync(userId);
            TryAddUser(user, emails);
        }

        return emails.ToList();
    }

    private async Task AddRoleEmailsAsync(
        string role,
        string? regionId,
        HashSet<string> emails,
        CancellationToken ct)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        foreach (var user in users)
        {
            if (user.IsDisabled)
            {
                continue;
            }

            // SaaS admins are platform-wide; regional staff must match the order's region.
            if (!string.IsNullOrWhiteSpace(regionId)
                && !string.Equals(user.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryAddUser(user, emails);
        }
    }

    private static void TryAddUser(ApplicationUser? user, HashSet<string> emails)
    {
        if (user is null || user.IsDisabled)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            emails.Add(user.Email.Trim());
        }
    }
}
