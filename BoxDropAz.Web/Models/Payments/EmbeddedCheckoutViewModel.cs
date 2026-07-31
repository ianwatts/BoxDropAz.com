namespace BoxDropAz.Web.Models.Payments;

public sealed class EmbeddedCheckoutViewModel
{
    public required string Title { get; init; }

    public required string Description { get; init; }

    public required string ClientSecret { get; init; }

    public required string PublishableKey { get; init; }

    public required string CancelUrl { get; init; }

    public string CancelLabel { get; init; } = "Cancel";

    public string? SummaryTitle { get; init; }

    public string? SummaryText { get; init; }
}
