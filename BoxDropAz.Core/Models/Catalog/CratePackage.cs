using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Catalog;

/// <summary>
/// A bookable bundle. Keyed by region so Phoenix and Tucson can price independently without a
/// code change; the SaaS admin edits these records directly.
/// </summary>
[DynamoDBTable(DynamoDbTableNames.CratePackage)]
public sealed class CratePackage
{
    [DynamoDBHashKey]
    [DynamoDBProperty("RegionId")]
    public required string RegionId { get; set; }

    [DynamoDBRangeKey]
    [DynamoDBProperty("PackageId")]
    public required string PackageId { get; set; }

    [DynamoDBProperty("Name")]
    public string Name { get; set; } = string.Empty;

    [DynamoDBProperty("Subtitle")]
    public string Subtitle { get; set; } = string.Empty;

    [DynamoDBProperty("CrateCount")]
    public int CrateCount { get; set; }

    [DynamoDBProperty("DollyCount")]
    public int DollyCount { get; set; }

    /// <summary>Price for the base rental period (7 days).</summary>
    [DynamoDBProperty("BasePriceCents")]
    public int BasePriceCents { get; set; }

    /// <summary>Charged per additional week, at booking or when extending mid-rental.</summary>
    [DynamoDBProperty("ExtraWeekPriceCents")]
    public int ExtraWeekPriceCents { get; set; }

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<string>>))]
    public List<string> IncludedItems { get; set; } = new();

    /// <summary>Optional ribbon, e.g. "Most popular".</summary>
    [DynamoDBProperty("Badge")]
    public string? Badge { get; set; }

    [DynamoDBProperty("SortOrder")]
    public int SortOrder { get; set; }

    [DynamoDBProperty("IsActive")]
    public bool IsActive { get; set; } = true;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
