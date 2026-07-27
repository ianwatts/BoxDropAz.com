using System.Security.Cryptography;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Web.Data;

namespace BoxDropAz.Web.Services;

public sealed class OrderService : IOrderService
{
    // Excludes vowels and lookalike characters so an order number read over the phone is unambiguous.
    private const string OrderNumberAlphabet = "23456789BCDFGHJKLMNPQRSTVWXZ";

    private readonly DynamoDbDataHelper _data;

    public OrderService(DynamoDbDataHelper data)
    {
        _data = data;
    }

    public async Task<RentalOrder?> GetAsync(string orderId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(orderId))
        {
            return null;
        }

        using var ctx = _data.CreateContext();
        return await ctx.LoadAsync<RentalOrder>(orderId, ct);
    }

    public async Task SaveAsync(RentalOrder order, CancellationToken ct = default)
    {
        order.UpdatedAtUtc = DateTime.UtcNow;

        // DynamoDB rejects empty strings on index key attributes, which would make an order
        // silently unsaveable. Fail loudly here instead.
        RequireIndexKey(order.OrderId, nameof(order.OrderId));
        RequireIndexKey(order.RegionId, nameof(order.RegionId));
        RequireIndexKey(order.UserId, nameof(order.UserId));
        RequireIndexKey(order.DeliveryDate, nameof(order.DeliveryDate));
        RequireIndexKey(order.PickupDate, nameof(order.PickupDate));

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(order, ct);
    }

    public async Task<List<RentalOrder>> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<RentalOrder>();
        }

        using var ctx = _data.CreateContext();
        var orders = await ctx.QueryAsync<RentalOrder>(userId, new QueryConfig
        {
            IndexName = DynamoDbTableNames.RentalOrderByUserIndex,
            BackwardQuery = true
        }).GetRemainingAsync(ct);

        return orders;
    }

    public Task<List<RentalOrder>> GetDeliveriesAsync(string regionId, DateOnly date, CancellationToken ct = default)
        => QueryByDateAsync(regionId, date, DynamoDbTableNames.RentalOrderByRegionAndDeliveryDateIndex, ct);

    public Task<List<RentalOrder>> GetPickupsAsync(string regionId, DateOnly date, CancellationToken ct = default)
        => QueryByDateAsync(regionId, date, DynamoDbTableNames.RentalOrderByRegionAndPickupDateIndex, ct);

    public async Task<List<RentalOrder>> GetCreatedBetweenAsync(string regionId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return new List<RentalOrder>();
        }

        using var ctx = _data.CreateContext();
        return await ctx.QueryAsync<RentalOrder>(
            regionId,
            QueryOperator.Between,
            new object[] { fromUtc, toUtc },
            new QueryConfig { IndexName = DynamoDbTableNames.RentalOrderByRegionAndCreatedIndex })
            .GetRemainingAsync(ct);
    }

    public async Task<List<RentalOrder>> GetRecentForRegionAsync(string regionId, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return new List<RentalOrder>();
        }

        using var ctx = _data.CreateContext();
        var orders = await ctx.QueryAsync<RentalOrder>(regionId, new QueryConfig
        {
            IndexName = DynamoDbTableNames.RentalOrderByRegionAndCreatedIndex,
            BackwardQuery = true
        }).GetRemainingAsync(ct);

        return limit > 0 ? orders.Take(limit).ToList() : orders;
    }

    public string GenerateOrderNumber()
    {
        Span<char> chars = stackalloc char[6];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = OrderNumberAlphabet[RandomNumberGenerator.GetInt32(OrderNumberAlphabet.Length)];
        }

        return $"BDA-{new string(chars)}";
    }

    private async Task<List<RentalOrder>> QueryByDateAsync(string regionId, DateOnly date, string indexName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(regionId))
        {
            return new List<RentalOrder>();
        }

        using var ctx = _data.CreateContext();
        var orders = await ctx.QueryAsync<RentalOrder>(
            regionId,
            QueryOperator.Equal,
            new object[] { date.ToString("yyyy-MM-dd") },
            new QueryConfig { IndexName = indexName })
            .GetRemainingAsync(ct);

        return orders;
    }

    private static void RequireIndexKey(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"RentalOrder.{name} is required because it is a DynamoDB index key.");
        }
    }
}
