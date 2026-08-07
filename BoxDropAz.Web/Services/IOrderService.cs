using BoxDropAz.Core.Models.Orders;

namespace BoxDropAz.Web.Services;

public interface IOrderService
{
    Task<RentalOrder?> GetAsync(string orderId, CancellationToken ct = default);

    Task SaveAsync(RentalOrder order, CancellationToken ct = default);

    /// <summary>Newest first, for the customer dashboard.</summary>
    Task<List<RentalOrder>> GetForUserAsync(string userId, CancellationToken ct = default);

    /// <summary>Orders being delivered on a date, for the worker manifest.</summary>
    Task<List<RentalOrder>> GetDeliveriesAsync(string regionId, DateOnly date, CancellationToken ct = default);

    /// <summary>Orders being collected on a date, for the worker manifest.</summary>
    Task<List<RentalOrder>> GetPickupsAsync(string regionId, DateOnly date, CancellationToken ct = default);

    /// <summary>Orders being delivered in an inclusive date range.</summary>
    Task<List<RentalOrder>> GetDeliveriesBetweenAsync(
        string regionId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>Orders being collected in an inclusive date range.</summary>
    Task<List<RentalOrder>> GetPickupsBetweenAsync(
        string regionId, DateOnly fromInclusive, DateOnly toInclusive, CancellationToken ct = default);

    /// <summary>Orders created inside a window, for admin revenue reporting.</summary>
    Task<List<RentalOrder>> GetCreatedBetweenAsync(string regionId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>Every order for a region, newest first. Used by the admin order list.</summary>
    Task<List<RentalOrder>> GetRecentForRegionAsync(string regionId, int limit, CancellationToken ct = default);

    string GenerateOrderNumber();
}
