namespace BoxDropAz.Core.Models.Catalog;

public sealed class AddOnOption
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required int UnitAmountCents { get; init; }

    public required int MaxQuantity { get; init; }
}

/// <summary>
/// Optional extras offered at checkout. Priced uniformly across regions, unlike packages.
/// </summary>
public static class AddOnCatalog
{
    public static readonly AddOnOption ExtraCrate = new()
    {
        Code = "extra-crate",
        Name = "Extra 27-gallon tote with lid",
        Description = "Add an individual 27-gallon tote with its snap-fit lid.",
        UnitAmountCents = 400,
        MaxQuantity = 40
    };

    public static readonly AddOnOption WardrobeCrate = new()
    {
        Code = "wardrobe-crate",
        Name = "Wardrobe tote",
        Description = "A reusable wardrobe container with a hanging rail.",
        UnitAmountCents = 600,
        MaxQuantity = 15
    };

    public static IReadOnlyList<AddOnOption> All { get; } = new[] { ExtraCrate, WardrobeCrate };

    public static AddOnOption? FromCode(string? code)
        => All.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
}
