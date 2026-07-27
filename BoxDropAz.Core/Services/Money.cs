using System.Globalization;

namespace BoxDropAz.Core.Services;

/// <summary>
/// All money in this app is integer cents. These helpers are the only place cents become text,
/// so rounding never drifts between the quote, the Stripe charge, and the receipt.
///
/// Formatted without CultureInfo because the Lambda linux-arm64 publish runs in
/// globalization-invariant mode (AL2023 has no ICU).
/// </summary>
public static class Money
{
    /// <summary>Formats as $1,234.56.</summary>
    public static string Format(int cents) => FormatCore(cents, forceCents: true);

    public static string Format(long cents) => FormatCore(cents, forceCents: true);

    /// <summary>Formats as $89 when the amount is whole dollars, otherwise $89.50.</summary>
    public static string FormatCompact(int cents)
        => FormatCore(cents, forceCents: cents % 100 != 0);

    public static decimal ToDollars(int cents) => cents / 100m;

    public static int FromDollars(decimal dollars) => (int)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);

    private static string FormatCore(long cents, bool forceCents)
    {
        var negative = cents < 0;
        var abs = negative ? -cents : cents;
        var dollars = abs / 100;
        var remainder = (int)(abs % 100);

        var dollarText = dollars.ToString("#,##0", CultureInfo.InvariantCulture);
        var body = forceCents || remainder != 0
            ? $"${dollarText}.{remainder:D2}"
            : $"${dollarText}";

        return negative ? $"({body})" : body;
    }
}
