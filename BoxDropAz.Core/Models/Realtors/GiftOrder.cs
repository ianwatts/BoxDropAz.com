using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Realtors;

public enum GiftStatus
{
    Sent = 0,
    Claimed = 1,
    Cancelled = 2,
    Expired = 3
}

[DynamoDBTable(DynamoDbTableNames.GiftOrder)]
public sealed class GiftOrder
{
    [DynamoDBHashKey]
    [DynamoDBProperty("GiftId")]
    public required string GiftId { get; set; }

    /// <summary>Unguessable token in the claim URL. Indexed so /gift/claim/{token} is a lookup.</summary>
    [DynamoDBGlobalSecondaryIndexHashKey(DynamoDbTableNames.GiftOrderByClaimTokenIndex)]
    [DynamoDBProperty("ClaimToken")]
    public string ClaimToken { get; set; } = string.Empty;

    [DynamoDBGlobalSecondaryIndexHashKey(DynamoDbTableNames.GiftOrderByRealtorIndex)]
    [DynamoDBProperty("RealtorUserId")]
    public string RealtorUserId { get; set; } = string.Empty;

    [DynamoDBProperty("RealtorName")]
    public string RealtorName { get; set; } = string.Empty;

    [DynamoDBProperty("RealtorCompany")]
    public string? RealtorCompany { get; set; }

    [DynamoDBProperty("RealtorEmail")]
    public string RealtorEmail { get; set; } = string.Empty;

    [DynamoDBProperty("RealtorPhone")]
    public string? RealtorPhone { get; set; }

    [DynamoDBProperty("RegionId")]
    public string RegionId { get; set; } = string.Empty;

    [DynamoDBProperty("ClientName")]
    public string ClientName { get; set; } = string.Empty;

    [DynamoDBProperty("ClientEmail")]
    public string ClientEmail { get; set; } = string.Empty;

    [DynamoDBProperty("ClientPhone")]
    public string? ClientPhone { get; set; }

    [DynamoDBProperty("PropertyAddressLine1")]
    public string PropertyAddressLine1 { get; set; } = string.Empty;

    [DynamoDBProperty("PropertyCity")]
    public string PropertyCity { get; set; } = string.Empty;

    [DynamoDBProperty("PropertyState")]
    public string PropertyState { get; set; } = "AZ";

    [DynamoDBProperty("PropertyZip")]
    public string PropertyZip { get; set; } = string.Empty;

    /// <summary>ISO date (yyyy-MM-dd).</summary>
    [DynamoDBProperty("ClosingDate")]
    public string ClosingDate { get; set; } = string.Empty;

    [DynamoDBProperty("GiftAmountCents")]
    public int GiftAmountCents { get; set; }

    [DynamoDBProperty("PersonalMessage")]
    public string? PersonalMessage { get; set; }

    [DynamoDBProperty("IncludeCoBrandingInsert")]
    public bool IncludeCoBrandingInsert { get; set; }

    [DynamoDBProperty("Status")]
    public GiftStatus Status { get; set; } = GiftStatus.Sent;

    [DynamoDBProperty("ClaimedAtUtc")]
    public DateTime? ClaimedAtUtc { get; set; }

    [DynamoDBProperty("RentalOrderId")]
    public string? RentalOrderId { get; set; }

    [DynamoDBProperty("CancelledAtUtc")]
    public DateTime? CancelledAtUtc { get; set; }

    [DynamoDBProperty("ExpiresAtUtc")]
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddDays(180);

    [DynamoDBGlobalSecondaryIndexRangeKey(DynamoDbTableNames.GiftOrderByRealtorIndex)]
    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBIgnore]
    public bool IsClaimable => Status == GiftStatus.Sent && ExpiresAtUtc > DateTime.UtcNow;
}
