using BoxDropAz.Core.Models.Realtors;

namespace BoxDropAz.Web.Services;

public interface ISubscriptionService
{
    Task<RealtorSubscription?> GetAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the existing record or an empty, unsaved one so callers never null check.</summary>
    Task<RealtorSubscription> GetOrCreateAsync(string userId, string regionId, CancellationToken ct = default);

    /// <summary>Reverse lookup used by webhooks, which identify an agent only by Stripe customer.</summary>
    Task<RealtorSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default);

    /// <summary>
    /// Every subscription record. Only the platform rollup needs this; it is a table scan, so it is
    /// deliberately not exposed anywhere a customer request can reach.
    /// </summary>
    Task<List<RealtorSubscription>> GetAllAsync(CancellationToken ct = default);

    Task SaveAsync(RealtorSubscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Adds the monthly allocation, capped at the plan's rollover ceiling. Safe to call twice for
    /// the same invoice: the second call is a no-op.
    /// </summary>
    Task<bool> GrantMonthlyCreditAsync(string userId, RealtorPlanId planId, string invoiceId, CancellationToken ct = default);

    /// <summary>
    /// Atomically debits credit. Returns false when the balance is insufficient, which is how two
    /// concurrent gift submissions are prevented from overdrawing the same balance.
    /// </summary>
    Task<(bool Success, int NewBalanceCents)> TryDeductCreditAsync(string userId, int amountCents, CancellationToken ct = default);

    /// <summary>Returns credit for a cancelled or expired gift.</summary>
    Task<int> RefundCreditAsync(string userId, int amountCents, string giftId, string description, CancellationToken ct = default);

    Task WriteLedgerEntryAsync(CreditLedgerEntry entry, CancellationToken ct = default);

    Task<List<CreditLedgerEntry>> GetLedgerAsync(string userId, int limit, CancellationToken ct = default);
}
