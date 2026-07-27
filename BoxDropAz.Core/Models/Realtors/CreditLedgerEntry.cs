using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Realtors;

public enum CreditEntryKind
{
    /// <summary>Monthly allocation from a paid invoice.</summary>
    Grant = 0,
    /// <summary>Credit consumed by sending a gift.</summary>
    Debit = 1,
    /// <summary>Credit returned when an unclaimed gift is cancelled.</summary>
    Refund = 2,
    /// <summary>Manual correction by an admin.</summary>
    Adjustment = 3
}

/// <summary>
/// Append only audit trail for every credit movement. The subscription record holds the running
/// balance; this table explains how it got there.
/// </summary>
[DynamoDBTable(DynamoDbTableNames.CreditLedger)]
public sealed class CreditLedgerEntry
{
    [DynamoDBHashKey]
    [DynamoDBProperty("UserId")]
    public required string UserId { get; set; }

    /// <summary>Sortable: UTC timestamp then a random suffix, so a query returns chronological order.</summary>
    [DynamoDBRangeKey]
    [DynamoDBProperty("EntryId")]
    public required string EntryId { get; set; }

    [DynamoDBProperty("Kind")]
    public CreditEntryKind Kind { get; set; }

    /// <summary>Signed: positive for grants and refunds, negative for debits.</summary>
    [DynamoDBProperty("AmountCents")]
    public int AmountCents { get; set; }

    [DynamoDBProperty("BalanceAfterCents")]
    public int BalanceAfterCents { get; set; }

    [DynamoDBProperty("Description")]
    public string Description { get; set; } = string.Empty;

    [DynamoDBProperty("RelatedGiftId")]
    public string? RelatedGiftId { get; set; }

    [DynamoDBProperty("RelatedInvoiceId")]
    public string? RelatedInvoiceId { get; set; }

    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string NewEntryId(DateTime utcNow)
        => $"{utcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
}
