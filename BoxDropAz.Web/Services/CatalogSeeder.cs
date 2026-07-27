using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Creates the launch regions and their crate catalog on first boot. Existing records are left
/// alone so operator pricing edits are never overwritten by a redeploy.
/// </summary>
public static class CatalogSeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var regionService = services.GetRequiredService<IRegionService>();
        var catalogService = services.GetRequiredService<ICatalogService>();

        foreach (var region in BuildRegions())
        {
            var existing = await regionService.GetByIdAsync(region.Id, ct);
            if (existing is null)
            {
                await regionService.SaveAsync(region, ct);
                Console.WriteLine($"Seeded region: {region.Name}");
            }

            var packages = await catalogService.GetAllPackagesAsync(region.Id, ct);
            if (packages.Count == 0)
            {
                foreach (var package in BuildPackages(region.Id))
                {
                    await catalogService.SavePackageAsync(package, ct);
                }
                Console.WriteLine($"Seeded crate catalog for {region.Name}");
            }
        }
    }

    private static IEnumerable<Region> BuildRegions()
    {
        yield return new Region
        {
            Id = "phoenix",
            Name = "Phoenix Metro",
            Slug = "phoenix",
            Description = "Serving the East Valley, Casa Grande, and Pinal County with free delivery and pickup in our core zone.",
            TimeZoneId = "US/Arizona",
            SupportPhone = "(480) 555-0142",
            IsActive = true,
            DamageFees = new DamageFeeSchedule(),
            DeliveryZones = new List<DeliveryZone>
            {
                new()
                {
                    Name = "Zone A",
                    Cities = "Maricopa, Casa Grande, Queen Creek, San Tan Valley, Gilbert, Chandler, Apache Junction, Florence, Coolidge",
                    SurchargeCents = 0,
                    ZipCodes = new List<string>
                    {
                        "85138", "85139", // Maricopa
                        "85122", "85193", "85194", // Casa Grande
                        "85140", "85142", // Queen Creek
                        "85143", "85144", // San Tan Valley
                        "85233", "85234", "85295", "85296", "85297", "85298", // Gilbert
                        "85224", "85225", "85226", "85248", "85249", "85286", // Chandler
                        "85117", "85119", "85120", // Apache Junction
                        "85132", // Florence
                        "85128"  // Coolidge
                    }
                },
                new()
                {
                    Name = "Zone B",
                    Cities = "Mesa, Tempe, Ahwatukee, Sun Lakes, Higley",
                    SurchargeCents = 2500,
                    ZipCodes = new List<string>
                    {
                        "85201", "85202", "85203", "85204", "85205", "85206", "85207", "85208", "85209", "85210",
                        "85212", "85213", "85215", // Mesa
                        "85281", "85282", "85283", "85284", // Tempe
                        "85044", "85045", "85048", // Ahwatukee
                        "85236", "85242" // Higley / Sun Lakes
                    }
                },
                new()
                {
                    Name = "Zone C",
                    Cities = "Central Phoenix, Scottsdale, Eloy, Arizona City, Superior",
                    SurchargeCents = 4500,
                    ZipCodes = new List<string>
                    {
                        "85003", "85004", "85006", "85008", "85012", "85014", "85016", "85018", "85028", "85032",
                        "85250", "85251", "85254", "85255", "85258", "85260", // Scottsdale
                        "85131", // Eloy
                        "85123", // Arizona City
                        "85173"  // Superior
                    }
                }
            }
        };

        yield return new Region
        {
            Id = "tucson",
            Name = "Tucson",
            Slug = "tucson",
            Description = "Crates delivered across metro Tucson, Oro Valley, Marana, and the Vail corridor.",
            TimeZoneId = "US/Arizona",
            SupportPhone = "(520) 555-0188",
            IsActive = true,
            DamageFees = new DamageFeeSchedule(),
            DeliveryZones = new List<DeliveryZone>
            {
                new()
                {
                    Name = "Zone A",
                    Cities = "Central and east Tucson, Catalina Foothills",
                    SurchargeCents = 0,
                    ZipCodes = new List<string>
                    {
                        "85701", "85704", "85710", "85711", "85712", "85715", "85716", "85718", "85719",
                        "85730", "85741", "85742", "85745", "85746", "85747", "85748", "85749", "85750"
                    }
                },
                new()
                {
                    Name = "Zone B",
                    Cities = "Oro Valley, Marana, Vail, Sahuarita",
                    SurchargeCents = 2500,
                    ZipCodes = new List<string> { "85737", "85755", "85653", "85658", "85641", "85629" }
                },
                new()
                {
                    Name = "Zone C",
                    Cities = "Green Valley, Catalina, Benson",
                    SurchargeCents = 4500,
                    ZipCodes = new List<string> { "85614", "85739", "85602" }
                }
            }
        };
    }

    private static IEnumerable<CratePackage> BuildPackages(string regionId)
    {
        yield return new CratePackage
        {
            RegionId = regionId,
            PackageId = "studio",
            Name = "Studio",
            Subtitle = "Studio or single room",
            CrateCount = 20,
            DollyCount = 1,
            BasePriceCents = 8900,
            ExtraWeekPriceCents = 4500,
            SortOrder = 1,
            IncludedItems = new List<string> { "20 stackable crates", "1 rolling dolly", "20 reusable labels", "Free delivery and pickup in Zone A" }
        };

        yield return new CratePackage
        {
            RegionId = regionId,
            PackageId = "small",
            Name = "1-2 Bedroom",
            Subtitle = "Apartments and condos",
            CrateCount = 35,
            DollyCount = 2,
            BasePriceCents = 12900,
            ExtraWeekPriceCents = 6500,
            SortOrder = 2,
            IncludedItems = new List<string> { "35 stackable crates", "2 rolling dollies", "35 reusable labels", "Free delivery and pickup in Zone A" }
        };

        yield return new CratePackage
        {
            RegionId = regionId,
            PackageId = "medium",
            Name = "2-3 Bedroom",
            Subtitle = "The most common move",
            CrateCount = 50,
            DollyCount = 2,
            BasePriceCents = 16900,
            ExtraWeekPriceCents = 8500,
            Badge = "Most popular",
            SortOrder = 3,
            IncludedItems = new List<string> { "50 stackable crates", "2 rolling dollies", "50 reusable labels", "3 lbs packing paper", "Free delivery and pickup in Zone A" }
        };

        yield return new CratePackage
        {
            RegionId = regionId,
            PackageId = "large",
            Name = "3-4 Bedroom",
            Subtitle = "Family homes",
            CrateCount = 75,
            DollyCount = 3,
            BasePriceCents = 21900,
            ExtraWeekPriceCents = 11000,
            SortOrder = 4,
            IncludedItems = new List<string> { "75 stackable crates", "3 rolling dollies", "75 reusable labels", "3 lbs packing paper", "Free delivery and pickup in Zone A" }
        };

        yield return new CratePackage
        {
            RegionId = regionId,
            PackageId = "xlarge",
            Name = "4-5 Bedroom",
            Subtitle = "Large homes and estates",
            CrateCount = 100,
            DollyCount = 4,
            BasePriceCents = 29900,
            ExtraWeekPriceCents = 15000,
            SortOrder = 5,
            IncludedItems = new List<string> { "100 stackable crates", "4 rolling dollies", "100 reusable labels", "6 lbs packing paper", "Free delivery and pickup in Zone A" }
        };
    }
}
