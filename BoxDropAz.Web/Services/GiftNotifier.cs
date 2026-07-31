using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Services;

namespace BoxDropAz.Web.Services;

/// <summary>Email for the realtor gifting loop: the client's claim link and the agent's receipt.</summary>
public sealed class GiftNotifier
{
    private readonly IEmailService _email;
    private readonly SiteUrls _urls;

    public GiftNotifier(IEmailService email, SiteUrls urls)
    {
        _email = email;
        _urls = urls;
    }

    public async Task SendClaimLinkAsync(GiftOrder gift, CancellationToken ct = default)
    {
        var claimUrl = _urls.GiftClaim(gift.ClaimToken);
        var fromLine = string.IsNullOrWhiteSpace(gift.RealtorCompany)
            ? gift.RealtorName
            : $"{gift.RealtorName}, {gift.RealtorCompany}";

        var message = string.IsNullOrWhiteSpace(gift.PersonalMessage)
            ? ""
            : $"<blockquote style=\"margin:20px 0;padding:12px 18px;border-left:3px solid #0d9488;color:#374151;\">" +
              $"{System.Net.WebUtility.HtmlEncode(gift.PersonalMessage)}<br />" +
              $"<span style=\"color:#6b7280;font-size:13px;\">&mdash; {fromLine}</span></blockquote>";

        var body = EmailTemplates.Wrap(
            $"{gift.RealtorName} sent you {Money.FormatCompact(gift.GiftAmountCents)} toward your move",
            $"<p>Congratulations on {gift.PropertyAddressLine1}{(string.IsNullOrWhiteSpace(gift.PropertyCity) ? "" : ", " + gift.PropertyCity)}.</p>" +
            message +
            $"<p>{fromLine} has covered {Money.FormatCompact(gift.GiftAmountCents)} of reusable moving totes for you. " +
            "No cardboard, no tape, no trip to the store. We deliver totes with lids and custom-fit dollies to your door, " +
            "then collect them when you're unpacked.</p>" +
            "<p>Pick your bundle and your dates whenever you're ready. If you choose something larger than the " +
            "gift covers, you just pay the difference.</p>",
            "Claim your totes",
            claimUrl);

        await _email.SendAsync(gift.ClientEmail, $"{gift.RealtorName} sent you a closing gift", body, ct);
    }

    public async Task SendAgentReceiptAsync(GiftOrder gift, int remainingBalanceCents, CancellationToken ct = default)
    {
        var body = EmailTemplates.Wrap(
            "Gift sent",
            $"<p>We emailed {gift.ClientName} their claim link.</p>" +
            EmailTemplates.DetailRows(
                ("Client", $"{gift.ClientName} ({gift.ClientEmail})"),
                ("Property", $"{gift.PropertyAddressLine1}, {gift.PropertyCity} {gift.PropertyZip}"),
                ("Closing", gift.ClosingDate),
                ("Gift value", Money.Format(gift.GiftAmountCents)),
                ("Credit remaining", Money.Format(remainingBalanceCents))) +
            "<p>You'll see the gift move to Claimed in your dashboard once they book. If they never claim it, " +
            "cancel it and the credit comes straight back.</p>",
            "Open my dashboard",
            _urls.AgentDashboard());

        await _email.SendAsync(gift.RealtorEmail, $"Closing gift sent to {gift.ClientName}", body, ct);
    }

    public async Task SendClaimedNoticeAsync(GiftOrder gift, string deliveryDate, CancellationToken ct = default)
    {
        var body = EmailTemplates.Wrap(
            "Your client claimed their gift",
            $"<p>{gift.ClientName} booked their moving totes. Delivery is set for {deliveryDate}.</p>" +
            (gift.IncludeCoBrandingInsert
                ? "<p>Your co-branded insert goes out with the delivery.</p>"
                : ""),
            "Open my dashboard",
            _urls.AgentDashboard());

        await _email.SendAsync(gift.RealtorEmail, $"{gift.ClientName} claimed their closing gift", body, ct);
    }
}
