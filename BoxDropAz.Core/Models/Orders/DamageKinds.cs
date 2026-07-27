using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Core.Models.Orders;

public sealed record DamageKind(string Code, string Label, string Hint, bool PerUnit);

/// <summary>
/// The chargeable outcomes a driver can report. Prices come from the region's fee schedule rather
/// than the form, so a worker can never set an amount.
/// </summary>
public static class DamageKinds
{
    public const string Crate = "Crate";
    public const string Dolly = "Dolly";
    public const string MissedPickup = "MissedPickup";
    public const string DeepClean = "DeepClean";

    public static IReadOnlyList<DamageKind> All { get; } = new[]
    {
        new DamageKind(Crate, "Damaged or missing crate", "Cracked walls, broken lid, or not returned", true),
        new DamageKind(Dolly, "Damaged or missing dolly", "Bent frame, seized wheel, or not returned", true),
        new DamageKind(MissedPickup, "Missed pickup", "Nobody home and the crates weren't accessible", false),
        new DamageKind(DeepClean, "Deep clean needed", "Paint, food, pet waste or odour inside the crate", true)
    };

    public static DamageKind? FromCode(string? code)
        => All.FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Unit price for a reported kind. Prefers the schedule snapshotted onto the order at checkout,
    /// so a renter is charged the fees they actually agreed to, not today's prices.
    /// </summary>
    public static int UnitAmountCents(string code, TermsAcceptance? acceptedTerms, DamageFeeSchedule current)
        => code switch
        {
            Crate => acceptedTerms?.CrateReplacementCents ?? current.CrateReplacementCents,
            Dolly => acceptedTerms?.DollyReplacementCents ?? current.DollyReplacementCents,
            MissedPickup => acceptedTerms?.MissedPickupCents ?? current.MissedPickupCents,
            DeepClean => acceptedTerms?.DeepCleanPerCrateCents ?? current.DeepCleanPerCrateCents,
            _ => 0
        };
}
