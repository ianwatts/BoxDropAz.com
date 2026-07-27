namespace BoxDropAz.Core.Models.Orders;

/// <summary>
/// Evidence that the renter accepted the rental agreement, captured server side at checkout.
/// The fee snapshot is stored alongside the acceptance so a later change to the region's fee
/// schedule cannot retroactively change what this renter agreed to.
/// </summary>
public sealed class TermsAcceptance
{
    public string TermsVersion { get; set; } = string.Empty;

    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;

    public string AcceptedFromIp { get; set; } = string.Empty;

    public string AcceptedUserAgent { get; set; } = string.Empty;

    public int CrateReplacementCents { get; set; }

    public int DollyReplacementCents { get; set; }

    public int MissedPickupCents { get; set; }

    public int DeepCleanPerCrateCents { get; set; }
}
