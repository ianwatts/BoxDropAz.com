namespace BoxDropAz.Core.Models.Orders;

public sealed class DamageLine
{
    public string DamageId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Crate, Dolly, MissedPickup or DeepClean.</summary>
    public string Kind { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int UnitAmountCents { get; set; }

    public int TotalCents => Quantity * UnitAmountCents;

    public string Description { get; set; } = string.Empty;

    public DamageChargeStatus Status { get; set; } = DamageChargeStatus.PendingReview;

    public string ReportedByUserId { get; set; } = string.Empty;

    public string ReportedByName { get; set; } = string.Empty;

    public DateTime ReportedAtUtc { get; set; } = DateTime.UtcNow;

    public string? ResolvedByUserId { get; set; }

    public DateTime? ResolvedAtUtc { get; set; }

    public string? StripePaymentIntentId { get; set; }

    public string? FailureReason { get; set; }
}
