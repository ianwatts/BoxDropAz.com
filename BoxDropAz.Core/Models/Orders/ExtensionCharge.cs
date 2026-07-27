namespace BoxDropAz.Core.Models.Orders;

public sealed class ExtensionCharge
{
    public string ExtensionId { get; set; } = Guid.NewGuid().ToString("N");

    public int AdditionalWeeks { get; set; }

    public int AmountCents { get; set; }

    /// <summary>Pickup date before this extension was applied, for auditing.</summary>
    public string PreviousPickupDate { get; set; } = string.Empty;

    public string NewPickupDate { get; set; } = string.Empty;

    public string? StripePaymentIntentId { get; set; }

    public bool Succeeded { get; set; }

    public string? FailureReason { get; set; }

    public string RequestedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
