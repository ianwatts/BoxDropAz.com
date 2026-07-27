namespace BoxDropAz.Core.Models.Regions;

/// <summary>
/// Fees charged to the card on file after pickup. Surfaced verbatim on the rental terms page and
/// in the checkout consent text, so the values shown to the renter and the values charged always
/// come from the same record.
/// </summary>
public sealed class DamageFeeSchedule
{
    public int CrateReplacementCents { get; set; } = 4000;

    public int DollyReplacementCents { get; set; } = 9500;

    public int MissedPickupCents { get; set; } = 2500;

    public int DeepCleanPerCrateCents { get; set; } = 300;
}
