using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Inventory;

[DynamoDBTable(DynamoDbTableNames.Inventory)]
public sealed class InventoryRecord
{
    public const string SummaryRecordId = "SUMMARY";
    public const string SummaryType = "summary";
    public const string RestockType = "restock";

    [DynamoDBHashKey]
    [DynamoDBProperty("RegionId")]
    public required string RegionId { get; set; }

    [DynamoDBRangeKey]
    [DynamoDBProperty("RecordId")]
    public required string RecordId { get; set; }

    [DynamoDBProperty("RecordType")]
    public string RecordType { get; set; } = SummaryType;

    [DynamoDBProperty("TotalTotes")]
    public int TotalTotes { get; set; }

    [DynamoDBProperty("TotalDollies")]
    public int TotalDollies { get; set; }

    [DynamoDBProperty("TotalIndexCards")]
    public int TotalIndexCards { get; set; }

    [DynamoDBProperty("TotalCardHolders")]
    public int TotalCardHolders { get; set; }

    [DynamoDBProperty("IsConfigured")]
    public bool IsConfigured { get; set; }

    [DynamoDBProperty("RequestedTotes")]
    public int RequestedTotes { get; set; }

    [DynamoDBProperty("RequestedDollies")]
    public int RequestedDollies { get; set; }

    [DynamoDBProperty("RequestedCardHolders")]
    public int RequestedCardHolders { get; set; }

    [DynamoDBProperty("RequestedCardHolderPacks")]
    public int RequestedCardHolderPacks { get; set; }

    [DynamoDBProperty("RequestedCardPacks")]
    public int RequestedCardPacks { get; set; }

    [DynamoDBProperty("NeededByDate")]
    public string? NeededByDate { get; set; }

    [DynamoDBProperty("ActionByDate")]
    public string? ActionByDate { get; set; }

    [DynamoDBProperty("Status")]
    public string Status { get; set; } = InventoryTaskStatus.Open;

    [DynamoDBProperty("Reason")]
    public string? Reason { get; set; }

    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("CompletedAtUtc")]
    public DateTime? CompletedAtUtc { get; set; }

    [DynamoDBProperty("CompletedByUserId")]
    public string? CompletedByUserId { get; set; }

    [DynamoDBProperty("CompletedByName")]
    public string? CompletedByName { get; set; }
}

public static class InventoryTaskStatus
{
    public const string Open = "open";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}
