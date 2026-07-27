using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Services;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Transactional email for the rental lifecycle. Kept separate from the controllers because the
/// same messages are sent from both the return-from-Stripe page and the webhook, whichever
/// arrives first.
/// </summary>
public sealed class OrderNotifier
{
    private readonly IEmailService _email;
    private readonly IConfiguration _config;

    public OrderNotifier(IEmailService email, IConfiguration config)
    {
        _email = email;
        _config = config;
    }

    public async Task SendOrderConfirmationAsync(RentalOrder order, string? dashboardUrl, CancellationToken ct = default)
    {
        var intro = order.Source == OrderSource.RealtorGift && !string.IsNullOrWhiteSpace(order.GiftingRealtorName)
            ? $"<p>Your crates are booked, courtesy of {order.GiftingRealtorName}. Here are the details.</p>"
            : "<p>Your crates are booked. Here's everything you need for delivery day.</p>";

        var rows = EmailTemplates.DetailRows(
            ("Order", order.OrderNumber),
            ("Bundle", $"{order.PackageName} ({order.CrateCount} crates, {order.DollyCount} dollies)"),
            ("Delivery", $"{FormatDate(order.DeliveryDate)}, {order.DeliveryWindow}"),
            ("Pickup", $"{FormatDate(order.PickupDate)}, {order.PickupWindow}"),
            ("Address", $"{order.DeliveryAddressLine1}, {order.DeliveryCity} {order.DeliveryZip}"),
            ("Rental length", $"{order.RentalWeeks} week{(order.RentalWeeks == 1 ? "" : "s")}"),
            ("Gift credit applied", order.GiftCreditAppliedCents > 0 ? $"-{Money.Format(order.GiftCreditAppliedCents)}" : ""),
            ("Total paid", Money.Format(order.AmountPaidCents)));

        var body = EmailTemplates.Wrap(
            "Your crates are on the way",
            intro + rows +
            "<p>Someone needs to be home at delivery so we can place the crates and hand over the dollies. " +
            "You don't need to be there for pickup &mdash; just leave everything stacked and accessible.</p>" +
            "<p>Need longer? You can extend by the week from your dashboard at any time.</p>",
            dashboardUrl is null ? null : "View my rental",
            dashboardUrl);

        await _email.SendAsync(order.CustomerEmail, $"Booking confirmed - {order.OrderNumber}", body, ct);
        await NotifyAdminAsync(
            $"New booking {order.OrderNumber} ({Money.Format(order.AmountPaidCents)})",
            EmailTemplates.Wrap("New booking", rows),
            ct);
    }

    public async Task SendExtensionReceiptAsync(RentalOrder order, ExtensionCharge extension, CancellationToken ct = default)
    {
        var body = EmailTemplates.Wrap(
            "Rental extended",
            "<p>We've extended your rental and moved your pickup date. Nothing else to do.</p>" +
            EmailTemplates.DetailRows(
                ("Order", order.OrderNumber),
                ("Added", $"{extension.AdditionalWeeks} week{(extension.AdditionalWeeks == 1 ? "" : "s")}"),
                ("New pickup date", FormatDate(extension.NewPickupDate)),
                ("Charged", Money.Format(extension.AmountCents))));

        await _email.SendAsync(order.CustomerEmail, $"Rental extended - {order.OrderNumber}", body, ct);
    }

    public async Task SendDamageChargeAsync(RentalOrder order, IReadOnlyList<DamageLine> charges, int totalCents, CancellationToken ct = default)
    {
        var lines = string.Concat(charges.Select(c =>
            $"<li>{c.Quantity} &times; {c.Kind} at {Money.Format(c.UnitAmountCents)} = <strong>{Money.Format(c.TotalCents)}</strong>" +
            (string.IsNullOrWhiteSpace(c.Description) ? "" : $"<br /><span style=\"color:#6b7280;font-size:13px;\">{c.Description}</span>") +
            "</li>"));

        var body = EmailTemplates.Wrap(
            "Equipment charge applied",
            "<p>After collecting your crates we applied the following charge to the card on file, " +
            "under section 5 of the rental agreement you accepted at checkout.</p>" +
            $"<ul>{lines}</ul>" +
            $"<p><strong>Total charged: {Money.Format(totalCents)}</strong></p>" +
            "<p>If you think this is wrong, reply to this email within 14 days and we'll review it " +
            "against the driver's report.</p>");

        await _email.SendAsync(order.CustomerEmail, $"Equipment charge - {order.OrderNumber}", body, ct);
    }

    public async Task SendCancellationAsync(RentalOrder order, string reason, CancellationToken ct = default)
    {
        var body = EmailTemplates.Wrap(
            "Booking cancelled",
            $"<p>Your booking {order.OrderNumber} has been cancelled.</p>" +
            (string.IsNullOrWhiteSpace(reason) ? "" : $"<p>Reason: {reason}</p>") +
            "<p>Any refund due will appear on your statement within 5 to 10 business days.</p>");

        await _email.SendAsync(order.CustomerEmail, $"Booking cancelled - {order.OrderNumber}", body, ct);
    }

    public async Task SendAccountSetupAsync(string email, string fullName, string setPasswordUrl, CancellationToken ct = default)
    {
        var body = EmailTemplates.Wrap(
            "Finish setting up your account",
            $"<p>Hi {fullName}, we created an account for you so you can track your rental, extend it, " +
            "and manage your card on file. Choose a password to get in.</p>",
            "Choose a password",
            setPasswordUrl);

        await _email.SendAsync(email, "Set your BoxDrop AZ password", body, ct);
    }

    private async Task NotifyAdminAsync(string subject, string body, CancellationToken ct)
    {
        var adminEmail = _config["Site:AdminEmail"];
        if (!string.IsNullOrWhiteSpace(adminEmail))
        {
            await _email.SendAsync(adminEmail, subject, body, ct);
        }
    }

    private static string FormatDate(string isoDate)
        => DateOnly.TryParse(isoDate, out var parsed)
            ? parsed.ToString("dddd, MMMM d, yyyy")
            : isoDate;
}
