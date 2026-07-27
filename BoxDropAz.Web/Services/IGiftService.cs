using BoxDropAz.Core.Models.Realtors;

namespace BoxDropAz.Web.Services;

public interface IGiftService
{
    Task<GiftOrder?> GetAsync(string giftId, CancellationToken ct = default);

    Task<GiftOrder?> GetByClaimTokenAsync(string claimToken, CancellationToken ct = default);

    Task<List<GiftOrder>> GetForRealtorAsync(string realtorUserId, CancellationToken ct = default);

    Task SaveAsync(GiftOrder gift, CancellationToken ct = default);

    string GenerateClaimToken();
}
