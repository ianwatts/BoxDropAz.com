using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Services;

public sealed class SesEmailService : IEmailService, IEmailSender<ApplicationUser>
{
    private readonly IAmazonSimpleEmailService _ses;
    private readonly IConfiguration _config;
    private readonly ILogger<SesEmailService> _logger;

    public SesEmailService(IAmazonSimpleEmailService ses, IConfiguration config, ILogger<SesEmailService> logger)
    {
        _ses = ses;
        _config = config;
        _logger = logger;
    }

    public async Task<bool> SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        var fromEmail = _config["AWS:SES:FromEmail"];
        var fromName = _config["AWS:SES:FromName"] ?? "BoxDrop AZ";

        if (string.IsNullOrWhiteSpace(fromEmail) || string.IsNullOrWhiteSpace(toEmail))
        {
            _logger.LogWarning("SES not configured; skipping email '{Subject}' to {To}", subject, toEmail);
            return false;
        }

        var request = new SendEmailRequest
        {
            Source = $"{fromName} <{fromEmail}>",
            Destination = new Destination { ToAddresses = new List<string> { toEmail } },
            Message = new Message
            {
                Subject = new Content(subject),
                Body = new Body { Html = new Content(htmlBody) }
            }
        };

        var configurationSet = _config["AWS:SES:ConfigurationSet"];
        if (!string.IsNullOrWhiteSpace(configurationSet))
        {
            request.ConfigurationSetName = configurationSet;
        }

        try
        {
            await _ses.SendEmailAsync(request, ct);
            return true;
        }
        catch (Exception ex)
        {
            // A failed notification must never fail the operation that triggered it.
            _logger.LogError(ex, "Failed to send email '{Subject}' to {To}", subject, toEmail);
            return false;
        }
    }

    // IEmailSender<ApplicationUser>

    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        LogLinkInDevelopment("Confirm email", email, confirmationLink);

        var body = EmailTemplates.Wrap(
            "Confirm your email",
            "<p>Welcome to BoxDrop AZ. Confirm your email address and your account is ready to go.</p>",
            "Confirm email",
            confirmationLink);

        return SendAsync(email, "Confirm your BoxDrop AZ account", body);
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink)
    {
        LogLinkInDevelopment("Password reset", email, resetLink);

        var body = EmailTemplates.Wrap(
            "Reset your password",
            "<p>We received a request to reset your BoxDrop AZ password. This link expires shortly. If you did not ask for it, you can ignore this email.</p>",
            "Reset password",
            resetLink);

        return SendAsync(email, "Reset your BoxDrop AZ password", body);
    }

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var body = EmailTemplates.Wrap(
            "Your password reset code",
            $"<p>Use this code to reset your password:</p><p style=\"font-size:24px;font-weight:700;letter-spacing:3px;\">{resetCode}</p>");

        return SendAsync(email, "Your BoxDrop AZ password reset code", body);
    }

    /// <summary>
    /// Without a verified SES sender there is no way to click through a confirmation link locally,
    /// so the link goes to the log instead.
    /// </summary>
    private void LogLinkInDevelopment(string purpose, string email, string link)
    {
        if (string.IsNullOrWhiteSpace(_config["AWS:SES:FromEmail"]))
        {
            _logger.LogInformation("{Purpose} link for {Email}: {Link}", purpose, email, link);
        }
    }
}
