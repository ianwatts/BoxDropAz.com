using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Billing;

/// <summary>
/// Webhook dedup marker. Written with a conditional put keyed on the Stripe event id, so a retried
/// delivery cannot re-run a handler. This matters most for invoice.paid, which grants realtor gift
/// credit and is not naturally idempotent.
/// </summary>
[DynamoDBTable(DynamoDbTableNames.StripeEvent)]
public sealed class StripeEventRecord
{
    [DynamoDBHashKey]
    [DynamoDBProperty("EventId")]
    public required string EventId { get; set; }

    [DynamoDBProperty("EventType")]
    public string EventType { get; set; } = string.Empty;

    [DynamoDBProperty("ReceivedAtUtc")]
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("ProcessedAtUtc")]
    public DateTime? ProcessedAtUtc { get; set; }

    [DynamoDBProperty("Outcome")]
    public string Outcome { get; set; } = string.Empty;

    [DynamoDBProperty("RelatedId")]
    public string? RelatedId { get; set; }

    [DynamoDBProperty("ErrorMessage")]
    public string? ErrorMessage { get; set; }
}
