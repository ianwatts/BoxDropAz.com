using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Web.Data;
using Microsoft.Extensions.Caching.Memory;

namespace BoxDropAz.Web.Services;

public sealed class CatalogService : ICatalogService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);

    private readonly DynamoDbDataHelper _data;
    private readonly IMemoryCache _cache;

    public CatalogService(DynamoDbDataHelper data, IMemoryCache cache)
    {
        _data = data;
        _cache = cache;
    }

    public async Task<List<CratePackage>> GetPackagesAsync(string regionId, CancellationToken ct = default)
        => (await GetAllPackagesAsync(regionId, ct)).Where(p => p.IsActive).ToList();

    public async Task<List<CratePackage>> GetAllPackagesAsync(string regionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return new List<CratePackage>();
        }

        var cacheKey = CacheKey(regionId);
        if (_cache.TryGetValue<List<CratePackage>>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        using var ctx = _data.CreateContext();
        var packages = await ctx.QueryAsync<CratePackage>(regionId).GetRemainingAsync(ct);
        packages = packages.OrderBy(p => p.SortOrder).ThenBy(p => p.BasePriceCents).ToList();

        _cache.Set(cacheKey, packages, CacheDuration);
        return packages;
    }

    public async Task<CratePackage?> GetPackageAsync(string regionId, string packageId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return null;
        }

        return (await GetAllPackagesAsync(regionId, ct)).FirstOrDefault(p => p.PackageId == packageId);
    }

    public async Task SavePackageAsync(CratePackage package, CancellationToken ct = default)
    {
        package.UpdatedAtUtc = DateTime.UtcNow;

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(package, ct);

        _cache.Remove(CacheKey(package.RegionId));
    }

    public async Task DeletePackageAsync(string regionId, string packageId, CancellationToken ct = default)
    {
        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync<CratePackage>(regionId, packageId, ct);

        _cache.Remove(CacheKey(regionId));
    }

    private static string CacheKey(string regionId) => $"packages:{regionId}";
}
