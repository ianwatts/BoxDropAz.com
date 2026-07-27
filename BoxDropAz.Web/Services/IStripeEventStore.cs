using BoxDropAz.Core.Models.Billing;

namespace BoxDropAz.Web.Services;

public interface IStripeEventStore
{
    /// <summary>
    /// Claims an event id for processing. Returns false when this delivery is a duplicate, which
    /// Stripe does routinely on retries and which must not re-run credit grants or charges.
    /// </summary>
    Task<bool> TryClaimAsync(string eventId, string eventType, CancellationToken ct = default);

    Task MarkProcessedAsync(string eventId, string outcome, string? relatedId = null, CancellationToken ct = default);

    Task MarkFailedAsync(string eventId, string error, CancellationToken ct = default);

    /// <summary>Releases a claim so a Stripe retry gets another attempt at a transient failure.</summary>
    Task ReleaseAsync(string eventId, CancellationToken ct = default);

    Task<List<StripeEventRecord>> GetRecentAsync(int limit, CancellationToken ct = default);
}
