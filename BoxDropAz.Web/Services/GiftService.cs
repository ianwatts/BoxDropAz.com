using System.Security.Cryptography;
using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Web.Data;
using Microsoft.AspNetCore.WebUtilities;

namespace BoxDropAz.Web.Services;

public sealed class GiftService : IGiftService
{
    private readonly DynamoDbDataHelper _data;

    public GiftService(DynamoDbDataHelper data)
    {
        _data = data;
    }

    public async Task<GiftOrder?> GetAsync(string giftId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(giftId))
        {
            return null;
        }

        using var ctx = _data.CreateContext();
        return await ctx.LoadAsync<GiftOrder>(giftId, ct);
    }

    public async Task<GiftOrder?> GetByClaimTokenAsync(string claimToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(claimToken))
        {
            return null;
        }

        using var ctx = _data.CreateContext();
        var matches = await ctx.QueryAsync<GiftOrder>(claimToken, new QueryConfig
        {
            IndexName = DynamoDbTableNames.GiftOrderByClaimTokenIndex
        }).GetRemainingAsync(ct);

        return matches.FirstOrDefault();
    }

    public async Task<List<GiftOrder>> GetForRealtorAsync(string realtorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(realtorUserId))
        {
            return new List<GiftOrder>();
        }

        using var ctx = _data.CreateContext();
        return await ctx.QueryAsync<GiftOrder>(realtorUserId, new QueryConfig
        {
            IndexName = DynamoDbTableNames.GiftOrderByRealtorIndex,
            BackwardQuery = true
        }).GetRemainingAsync(ct);
    }

    public async Task SaveAsync(GiftOrder gift, CancellationToken ct = default)
    {
        gift.UpdatedAtUtc = DateTime.UtcNow;

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(gift, ct);
    }

    /// <summary>
    /// 256 bits of entropy in the claim URL. The link is the only thing standing between a
    /// stranger and someone else's gift credit, so it has to be unguessable.
    /// </summary>
    public string GenerateClaimToken()
        => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
}
