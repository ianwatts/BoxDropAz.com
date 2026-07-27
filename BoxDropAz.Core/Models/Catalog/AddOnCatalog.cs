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
        Name = "Extra crate",
        Description = "Add individual crates on top of your bundle.",
        UnitAmountCents = 400,
        MaxQuantity = 40
    };

    public static readonly AddOnOption PackingPaper = new()
    {
        Code = "packing-paper",
        Name = "Packing paper bundle",
        Description = "3 lbs of recycled newsprint, about 100 sheets.",
        UnitAmountCents = 1500,
        MaxQuantity = 10
    };

    public static readonly AddOnOption WardrobeCrate = new()
    {
        Code = "wardrobe-crate",
        Name = "Wardrobe crate",
        Description = "Hanging rail so closets move straight across.",
        UnitAmountCents = 600,
        MaxQuantity = 15
    };

    public static IReadOnlyList<AddOnOption> All { get; } = new[] { ExtraCrate, PackingPaper, WardrobeCrate };

    public static AddOnOption? FromCode(string? code)
        => All.FirstOrDefault(a => string.Equals(a.Code, code, StringComparison.OrdinalIgnoreCase));
}
