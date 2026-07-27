using BoxDropAz.Core.Models.Catalog;

namespace BoxDropAz.Web.Services;

public interface ICatalogService
{
    /// <summary>Active packages for a region, in display order.</summary>
    Task<List<CratePackage>> GetPackagesAsync(string regionId, CancellationToken ct = default);

    /// <summary>Every package including inactive ones, for the admin editor.</summary>
    Task<List<CratePackage>> GetAllPackagesAsync(string regionId, CancellationToken ct = default);

    Task<CratePackage?> GetPackageAsync(string regionId, string packageId, CancellationToken ct = default);

    Task SavePackageAsync(CratePackage package, CancellationToken ct = default);

    Task DeletePackageAsync(string regionId, string packageId, CancellationToken ct = default);
}
