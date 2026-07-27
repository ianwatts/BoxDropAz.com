using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using Amazon.DynamoDBv2.Model;
using BoxDropAz.Core.Data;
using BoxDropAz.Core.Models.Realtors;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Data;

namespace BoxDropAz.Web.Services;

public sealed class SubscriptionService : ISubscriptionService
{
    private readonly DynamoDbDataHelper _data;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(DynamoDbDataHelper data, ILogger<SubscriptionService> logger)
    {
        _data = data;
        _logger = logger;
    }

    public async Task<RealtorSubscription?> GetAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        using var ctx = _data.CreateContext();
        return await ctx.LoadAsync<RealtorSubscription>(userId, ct);
    }

    public async Task<RealtorSubscription> GetOrCreateAsync(string userId, string regionId, CancellationToken ct = default)
    {
        var existing = await GetAsync(userId, ct);
        if (existing is not null)
        {
            return existing;
        }

        return new RealtorSubscription
        {
            UserId = userId,
            RegionId = regionId,
            PlanId = RealtorPlanId.None,
            PlanName = "None",
            Status = "none"
        };
    }

    public async Task<RealtorSubscription?> GetByStripeCustomerIdAsync(string stripeCustomerId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
        {
            return null;
        }

        using var ctx = _data.CreateContext();
        var matches = await ctx.ScanAsync<RealtorSubscription>(new List<ScanCondition>
        {
            new("StripeCustomerId", ScanOperator.Equal, stripeCustomerId)
        }).GetRemainingAsync(ct);

        return matches.FirstOrDefault();
    }

    public async Task<List<RealtorSubscription>> GetAllAsync(CancellationToken ct = default)
    {
        using var ctx = _data.CreateContext();
        return await ctx.ScanAsync<RealtorSubscription>(new List<ScanCondition>()).GetRemainingAsync(ct);
    }

    public async Task SaveAsync(RealtorSubscription subscription, CancellationToken ct = default)
    {
        subscription.UpdatedAtUtc = DateTime.UtcNow;

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(subscription, ct);
    }

    public async Task<bool> GrantMonthlyCreditAsync(string userId, RealtorPlanId planId, string invoiceId, CancellationToken ct = default)
    {
        var plan = RealtorPlan.FromId(planId);
        if (plan is null)
        {
            return false;
        }

        var subscription = await GetAsync(userId, ct);
        if (subscription is null)
        {
            _logger.LogWarning("Cannot grant credit: no subscription record for {UserId}", userId);
            return false;
        }

        // Rollover is capped, so a dormant agent's balance cannot grow without limit.
        var newBalance = Math.Min(subscription.CreditBalanceCents + plan.MonthlyCreditCents, plan.CreditCapCents);
        var actuallyGranted = newBalance - subscription.CreditBalanceCents;

        subscription.CreditBalanceCents = newBalance;
        subscription.MonthlyCreditCents = plan.MonthlyCreditCents;
        subscription.CreditCapCents = plan.CreditCapCents;
        subscription.LifetimeCreditGrantedCents += actuallyGranted;
        subscription.LastCreditGrantedAtUtc = DateTime.UtcNow;

        await SaveAsync(subscription, ct);

        await WriteLedgerEntryAsync(new CreditLedgerEntry
        {
            UserId = userId,
            EntryId = CreditLedgerEntry.NewEntryId(DateTime.UtcNow),
            Kind = CreditEntryKind.Grant,
            AmountCents = actuallyGranted,
            BalanceAfterCents = newBalance,
            Description = actuallyGranted < plan.MonthlyCreditCents
                ? $"{plan.Name} monthly credit (capped at the {Money.FormatCompact(plan.CreditCapCents)} rollover limit)"
                : $"{plan.Name} monthly credit",
            RelatedInvoiceId = invoiceId
        }, ct);

        return true;
    }

    public async Task<(bool Success, int NewBalanceCents)> TryDeductCreditAsync(string userId, int amountCents, CancellationToken ct = default)
    {
        if (amountCents <= 0)
        {
            var current = await GetAsync(userId, ct);
            return (true, current?.CreditBalanceCents ?? 0);
        }

        var tableName = DynamoDbTableNames.GetTableName(DynamoDbTableNames.RealtorSubscription);

        var request = new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue> { ["UserId"] = new() { S = userId } },
            UpdateExpression =
                "SET CreditBalanceCents = CreditBalanceCents - :amt, " +
                "LifetimeCreditSpentCents = if_not_exists(LifetimeCreditSpentCents, :zero) + :amt, " +
                "GiftsSent = if_not_exists(GiftsSent, :zero) + :one",
            // The balance check and the decrement happen in one atomic operation, so two gifts
            // submitted at the same moment cannot both pass a read-then-write check.
            ConditionExpression = "attribute_exists(UserId) AND CreditBalanceCents >= :amt",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":amt"] = new() { N = amountCents.ToString() },
                [":zero"] = new() { N = "0" },
                [":one"] = new() { N = "1" }
            },
            ReturnValues = ReturnValue.ALL_NEW
        };

        try
        {
            var response = await _data.Client.UpdateItemAsync(request, ct);
            var newBalance = response.Attributes.TryGetValue("CreditBalanceCents", out var value)
                ? int.Parse(value.N)
                : 0;

            return (true, newBalance);
        }
        catch (ConditionalCheckFailedException)
        {
            var current = await GetAsync(userId, ct);
            return (false, current?.CreditBalanceCents ?? 0);
        }
    }

    public async Task<int> RefundCreditAsync(string userId, int amountCents, string giftId, string description, CancellationToken ct = default)
    {
        if (amountCents <= 0)
        {
            var unchanged = await GetAsync(userId, ct);
            return unchanged?.CreditBalanceCents ?? 0;
        }

        var tableName = DynamoDbTableNames.GetTableName(DynamoDbTableNames.RealtorSubscription);

        var response = await _data.Client.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = tableName,
            Key = new Dictionary<string, AttributeValue> { ["UserId"] = new() { S = userId } },
            UpdateExpression =
                "SET CreditBalanceCents = CreditBalanceCents + :amt, " +
                "LifetimeCreditSpentCents = if_not_exists(LifetimeCreditSpentCents, :zero) - :amt",
            ConditionExpression = "attribute_exists(UserId)",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":amt"] = new() { N = amountCents.ToString() },
                [":zero"] = new() { N = "0" }
            },
            ReturnValues = ReturnValue.ALL_NEW
        }, ct);

        var newBalance = response.Attributes.TryGetValue("CreditBalanceCents", out var value)
            ? int.Parse(value.N)
            : 0;

        await WriteLedgerEntryAsync(new CreditLedgerEntry
        {
            UserId = userId,
            EntryId = CreditLedgerEntry.NewEntryId(DateTime.UtcNow),
            Kind = CreditEntryKind.Refund,
            AmountCents = amountCents,
            BalanceAfterCents = newBalance,
            Description = description,
            RelatedGiftId = giftId
        }, ct);

        return newBalance;
    }

    public async Task WriteLedgerEntryAsync(CreditLedgerEntry entry, CancellationToken ct = default)
    {
        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(entry, ct);
    }

    public async Task<List<CreditLedgerEntry>> GetLedgerAsync(string userId, int limit, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return new List<CreditLedgerEntry>();
        }

        using var ctx = _data.CreateContext();
        var entries = await ctx.QueryAsync<CreditLedgerEntry>(userId, new QueryConfig
        {
            BackwardQuery = true
        }).GetRemainingAsync(ct);

        return limit > 0 ? entries.Take(limit).ToList() : entries;
    }
}
