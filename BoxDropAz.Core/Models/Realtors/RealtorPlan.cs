namespace BoxDropAz.Core.Models.Realtors;

public enum RealtorPlanId
{
    None = 0,
    Starter = 1,
    Professional = 2,
    Brokerage = 3
}

/// <summary>
/// Static plan catalog. The dollar figures live here rather than in DynamoDB because each tier is
/// bound to a Stripe price id, so changing one without the other would desync billing.
/// </summary>
public sealed class RealtorPlan
{
    public required RealtorPlanId Id { get; init; }

    public required string Name { get; init; }

    public required string Tagline { get; init; }

    public required int MonthlyPriceCents { get; init; }

    /// <summary>Gift credit granted each time an invoice is paid.</summary>
    public required int MonthlyCreditCents { get; init; }

    public required int SeatCount { get; init; }

    public required bool CoBrandingEnabled { get; init; }

    public required string[] Features { get; init; }

    /// <summary>Configuration key holding this plan's Stripe price id.</summary>
    public required string PriceIdConfigKey { get; init; }

    /// <summary>Unused credit stops accruing at this multiple of the monthly allocation.</summary>
    public int CreditCapCents => MonthlyCreditCents * 3;

    public static readonly RealtorPlan Starter = new()
    {
        Id = RealtorPlanId.Starter,
        Name = "Starter",
        Tagline = "For agents closing a few homes a quarter",
        MonthlyPriceCents = 5900,
        MonthlyCreditCents = 7500,
        SeatCount = 1,
        CoBrandingEnabled = false,
        PriceIdConfigKey = "Stripe:RealtorStarterMonthlyPriceId",
        Features = new[]
        {
            "$75 in closing gift credit every month",
            "Unused credit rolls over up to 3 months",
            "Your client books online with one link",
            "Delivery and pickup handled for you"
        }
    };

    public static readonly RealtorPlan Professional = new()
    {
        Id = RealtorPlanId.Professional,
        Name = "Professional",
        Tagline = "For full time agents who close every month",
        MonthlyPriceCents = 12900,
        MonthlyCreditCents = 17500,
        SeatCount = 1,
        CoBrandingEnabled = true,
        PriceIdConfigKey = "Stripe:RealtorProfessionalMonthlyPriceId",
        Features = new[]
        {
            "$175 in closing gift credit every month",
            "Co-branded insert packed with every delivery",
            "Unused credit rolls over up to 3 months",
            "Gift history and delivery tracking"
        }
    };

    public static readonly RealtorPlan Brokerage = new()
    {
        Id = RealtorPlanId.Brokerage,
        Name = "Brokerage",
        Tagline = "For teams that want one bill and shared credit",
        MonthlyPriceCents = 29900,
        MonthlyCreditCents = 42500,
        SeatCount = 5,
        CoBrandingEnabled = true,
        PriceIdConfigKey = "Stripe:RealtorBrokerageMonthlyPriceId",
        Features = new[]
        {
            "$425 in closing gift credit every month",
            "5 agent seats sharing one credit pool",
            "Co-branded inserts and a printable flyer",
            "Priority scheduling on closing days"
        }
    };

    public static IReadOnlyList<RealtorPlan> All { get; } = new[] { Starter, Professional, Brokerage };

    public static RealtorPlan? FromId(RealtorPlanId id) => All.FirstOrDefault(p => p.Id == id);
}
