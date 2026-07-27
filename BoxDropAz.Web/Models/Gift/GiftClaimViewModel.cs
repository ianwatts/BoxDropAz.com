using BoxDropAz.Core.Models.Catalog;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Models.Regions;

namespace BoxDropAz.Web.Models.Gift;

public sealed class GiftClaimViewModel
{
    public required GiftOrder Gift { get; set; }

    public Region? Region { get; set; }

    public List<CratePackage> Packages { get; set; } = new();

    /// <summary>The largest bundle the gift covers outright, used to make the value concrete.</summary>
    public CratePackage? FullyCoveredPackage { get; set; }
}

public sealed class GiftUnavailableViewModel
{
    public required string Reason { get; set; }

    public string? AgentName { get; set; }
}
