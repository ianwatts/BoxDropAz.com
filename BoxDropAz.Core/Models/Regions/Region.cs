using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Regions;

// The environment prefix is applied on top of this name by DynamoDBContext.TableNamePrefix.
[DynamoDBTable(DynamoDbTableNames.Region)]
public sealed class Region
{
    [DynamoDBHashKey]
    [DynamoDBProperty("Id")]
    public required string Id { get; set; }

    [DynamoDBProperty("Name")]
    public string Name { get; set; } = string.Empty;

    [DynamoDBProperty("Slug")]
    public string Slug { get; set; } = string.Empty;

    /// <summary>Marketing blurb shown on the service area page.</summary>
    [DynamoDBProperty("Description")]
    public string Description { get; set; } = string.Empty;

    [DynamoDBProperty("TimeZoneId")]
    public string TimeZoneId { get; set; } = "US/Arizona";

    [DynamoDBProperty("SupportPhone")]
    public string SupportPhone { get; set; } = string.Empty;

    [DynamoDBProperty("IsActive")]
    public bool IsActive { get; set; } = true;

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<DeliveryZone>>))]
    public List<DeliveryZone> DeliveryZones { get; set; } = new();

    [DynamoDBProperty(typeof(JsonPropertyConverter<DamageFeeSchedule>))]
    public DamageFeeSchedule DamageFees { get; set; } = new();

    [DynamoDBProperty(typeof(JsonPropertyConverter<SchedulingSettings>))]
    public SchedulingSettings Scheduling { get; set; } = new();

    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Finds the zone serving a ZIP code, or null when the ZIP is outside the region entirely.
    /// </summary>
    public DeliveryZone? FindZoneForZip(string? zip)
    {
        if (string.IsNullOrWhiteSpace(zip))
        {
            return null;
        }

        var normalized = zip.Trim();
        if (normalized.Length > 5)
        {
            normalized = normalized[..5];
        }

        return DeliveryZones.FirstOrDefault(z => z.ZipCodes.Contains(normalized, StringComparer.OrdinalIgnoreCase));
    }
}
