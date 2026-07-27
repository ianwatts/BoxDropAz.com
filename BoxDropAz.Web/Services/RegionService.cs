using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Models.Regions;
using BoxDropAz.Web.Data;
using Microsoft.Extensions.Caching.Memory;

namespace BoxDropAz.Web.Services;

public sealed class RegionService : IRegionService
{
    private const string CacheKey = "regions:all";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly DynamoDbDataHelper _data;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;

    public RegionService(DynamoDbDataHelper data, IMemoryCache cache, IConfiguration config)
    {
        _data = data;
        _cache = cache;
        _config = config;
    }

    public async Task<List<Region>> GetAllAsync(CancellationToken ct = default)
    {
        if (_cache.TryGetValue<List<Region>>(CacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        using var ctx = _data.CreateContext();
        var regions = await ctx.ScanAsync<Region>(new List<ScanCondition>()).GetRemainingAsync(ct);
        regions = regions.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        _cache.Set(CacheKey, regions, CacheDuration);
        return regions;
    }

    public async Task<List<Region>> GetActiveAsync(CancellationToken ct = default)
        => (await GetAllAsync(ct)).Where(r => r.IsActive).ToList();

    public async Task<Region?> GetByIdAsync(string regionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return null;
        }

        return (await GetAllAsync(ct)).FirstOrDefault(r => r.Id == regionId);
    }

    public async Task<Region?> GetBySlugAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        return (await GetAllAsync(ct))
            .FirstOrDefault(r => string.Equals(r.Slug, slug, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(Region? Region, DeliveryZone? Zone)> ResolveZipAsync(string? zip, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(zip))
        {
            return (null, null);
        }

        foreach (var region in await GetActiveAsync(ct))
        {
            var zone = region.FindZoneForZip(zip);
            if (zone is not null)
            {
                return (region, zone);
            }
        }

        return (null, null);
    }

    public async Task<Region?> GetDefaultAsync(CancellationToken ct = default)
    {
        var defaultId = _config.GetValue<string>("Site:DefaultRegionId", "phoenix");
        var active = await GetActiveAsync(ct);
        return active.FirstOrDefault(r => r.Id == defaultId) ?? active.FirstOrDefault();
    }

    public async Task SaveAsync(Region region, CancellationToken ct = default)
    {
        region.UpdatedAtUtc = DateTime.UtcNow;

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(region, ct);

        _cache.Remove(CacheKey);
    }

    public async Task DeleteAsync(string regionId, CancellationToken ct = default)
    {
        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync<Region>(regionId, ct);

        _cache.Remove(CacheKey);
    }
}
