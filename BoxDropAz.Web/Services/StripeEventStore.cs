using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Models.Billing;
using BoxDropAz.Web.Data;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Idempotency ledger for Stripe webhooks, backed by a conditional put on the event id.
/// </summary>
public sealed class StripeEventStore : IStripeEventStore
{
    private readonly DynamoDbDataHelper _data;
    private readonly ILogger<StripeEventStore> _logger;

    public StripeEventStore(DynamoDbDataHelper data, ILogger<StripeEventStore> logger)
    {
        _data = data;
        _logger = logger;
    }

    public async Task<bool> TryClaimAsync(string eventId, string eventType, CancellationToken ct = default)
    {
        var request = new PutItemRequest
        {
            TableName = DynamoDbTableNames.GetTableName(DynamoDbTableNames.StripeEvent),
            Item = new Dictionary<string, AttributeValue>
            {
                ["EventId"] = new() { S = eventId },
                ["EventType"] = new() { S = eventType },
                ["ReceivedAtUtc"] = new() { S = DateTime.UtcNow.ToString("O") },
                ["Outcome"] = new() { S = "processing" }
            },
            // Whoever writes the marker first owns the event. A concurrent or retried delivery
            // fails here and returns without touching money.
            ConditionExpression = "attribute_not_exists(EventId)"
        };

        try
        {
            await _data.Client.PutItemAsync(request, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            _logger.LogInformation("Ignoring duplicate Stripe event {EventId} ({EventType})", eventId, eventType);
            return false;
        }
    }

    public async Task MarkProcessedAsync(string eventId, string outcome, string? relatedId = null, CancellationToken ct = default)
        => await UpdateOutcomeAsync(eventId, outcome, relatedId, null, ct);

    public async Task MarkFailedAsync(string eventId, string error, CancellationToken ct = default)
        => await UpdateOutcomeAsync(eventId, "failed", null, error, ct);

    public async Task ReleaseAsync(string eventId, CancellationToken ct = default)
    {
        try
        {
            await _data.Client.DeleteItemAsync(
                DynamoDbTableNames.GetTableName(DynamoDbTableNames.StripeEvent),
                new Dictionary<string, AttributeValue> { ["EventId"] = new() { S = eventId } },
                ct);
        }
        catch (Exception ex)
        {
            // Leaving the marker in place only costs us a replay we would have skipped anyway.
            _logger.LogWarning(ex, "Could not release Stripe event claim {EventId}", eventId);
        }
    }

    public async Task<List<StripeEventRecord>> GetRecentAsync(int limit, CancellationToken ct = default)
    {
        using var ctx = _data.CreateContext();
        var all = await ctx.ScanAsync<StripeEventRecord>(new List<ScanCondition>()).GetRemainingAsync(ct);

        return all
            .OrderByDescending(e => e.ReceivedAtUtc)
            .Take(limit > 0 ? limit : 100)
            .ToList();
    }

    private async Task UpdateOutcomeAsync(string eventId, string outcome, string? relatedId, string? error, CancellationToken ct)
    {
        var values = new Dictionary<string, AttributeValue>
        {
            [":outcome"] = new() { S = outcome },
            [":processed"] = new() { S = DateTime.UtcNow.ToString("O") }
        };

        var expression = "SET Outcome = :outcome, ProcessedAtUtc = :processed";

        if (!string.IsNullOrWhiteSpace(relatedId))
        {
            expression += ", RelatedId = :related";
            values[":related"] = new AttributeValue { S = relatedId };
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            expression += ", ErrorMessage = :error";
            values[":error"] = new AttributeValue { S = Truncate(error, 900) };
        }

        try
        {
            await _data.Client.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = DynamoDbTableNames.GetTableName(DynamoDbTableNames.StripeEvent),
                Key = new Dictionary<string, AttributeValue> { ["EventId"] = new() { S = eventId } },
                UpdateExpression = expression,
                ExpressionAttributeValues = values
            }, ct);
        }
        catch (Exception ex)
        {
            // The audit trail is not worth failing the webhook over; Stripe would just retry.
            _logger.LogWarning(ex, "Could not record outcome for Stripe event {EventId}", eventId);
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
