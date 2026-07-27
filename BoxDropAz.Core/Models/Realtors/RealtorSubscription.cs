using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Realtors;

[DynamoDBTable(DynamoDbTableNames.RealtorSubscription)]
public sealed class RealtorSubscription
{
    [DynamoDBHashKey]
    [DynamoDBProperty("UserId")]
    public required string UserId { get; set; }

    [DynamoDBProperty("PlanId")]
    public RealtorPlanId PlanId { get; set; } = RealtorPlanId.None;

    [DynamoDBProperty("PlanName")]
    public string PlanName { get; set; } = "None";

    [DynamoDBProperty("RegionId")]
    public string RegionId { get; set; } = string.Empty;

    [DynamoDBProperty("StripeCustomerId")]
    public string? StripeCustomerId { get; set; }

    [DynamoDBProperty("StripeSubscriptionId")]
    public string? StripeSubscriptionId { get; set; }

    /// <summary>Mirrors the Stripe subscription status, e.g. active, past_due, canceled.</summary>
    [DynamoDBProperty("Status")]
    public string Status { get; set; } = "none";

    [DynamoDBProperty("CreditBalanceCents")]
    public int CreditBalanceCents { get; set; }

    [DynamoDBProperty("MonthlyCreditCents")]
    public int MonthlyCreditCents { get; set; }

    [DynamoDBProperty("CreditCapCents")]
    public int CreditCapCents { get; set; }

    [DynamoDBProperty("SeatCount")]
    public int SeatCount { get; set; } = 1;

    [DynamoDBProperty("CoBrandingEnabled")]
    public bool CoBrandingEnabled { get; set; }

    [DynamoDBProperty("CurrentPeriodEndUtc")]
    public DateTime? CurrentPeriodEndUtc { get; set; }

    [DynamoDBProperty("LastCreditGrantedAtUtc")]
    public DateTime? LastCreditGrantedAtUtc { get; set; }

    /// <summary>Total gifted to date, shown on the agent dashboard.</summary>
    [DynamoDBProperty("LifetimeCreditGrantedCents")]
    public long LifetimeCreditGrantedCents { get; set; }

    [DynamoDBProperty("LifetimeCreditSpentCents")]
    public long LifetimeCreditSpentCents { get; set; }

    [DynamoDBProperty("GiftsSent")]
    public int GiftsSent { get; set; }

    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBIgnore]
    public bool IsActive => Status is "active" or "trialing";
}
