using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Services;

public interface IRegionService
{
    Task<List<Region>> GetAllAsync(CancellationToken ct = default);

    Task<List<Region>> GetActiveAsync(CancellationToken ct = default);

    Task<Region?> GetByIdAsync(string regionId, CancellationToken ct = default);

    Task<Region?> GetBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>Resolves the serving region and zone for a ZIP, or nulls when nobody covers it.</summary>
    Task<(Region? Region, DeliveryZone? Zone)> ResolveZipAsync(string? zip, CancellationToken ct = default);

    /// <summary>The region a visitor books against when they have not chosen one.</summary>
    Task<Region?> GetDefaultAsync(CancellationToken ct = default);

    Task SaveAsync(Region region, CancellationToken ct = default);

    Task DeleteAsync(string regionId, CancellationToken ct = default);
}
