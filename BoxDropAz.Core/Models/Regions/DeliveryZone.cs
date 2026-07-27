namespace BoxDropAz.Core.Models.Regions;

public sealed class DeliveryZone
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Human readable list of the cities this zone covers, for the service area page.</summary>
    public string Cities { get; set; } = string.Empty;

    public List<string> ZipCodes { get; set; } = new();

    /// <summary>Round trip delivery and pickup surcharge. Zone A is free.</summary>
    public int SurchargeCents { get; set; }
}
