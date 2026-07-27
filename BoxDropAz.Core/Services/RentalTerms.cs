namespace BoxDropAz.Core.Services;

public static class RentalTerms
{
    /// <summary>
    /// Bumped whenever the agreement text or fee structure changes. Stored on every order so we can
    /// prove which version a renter accepted.
    /// </summary>
    public const string CurrentVersion = "2026-01-v1";

    public const int BaseRentalDays = 7;

    /// <summary>Free cancellation cutoff, after which the route is already planned.</summary>
    public const int FreeCancellationHours = 48;

    public const int LateCancellationFeeCents = 2500;
}
