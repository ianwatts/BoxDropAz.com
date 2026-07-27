namespace BoxDropAz.Core.Models.Orders;

public sealed class AddOnLine
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public int UnitAmountCents { get; set; }

    public int TotalCents => Quantity * UnitAmountCents;
}
