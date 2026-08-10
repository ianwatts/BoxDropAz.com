using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Core.Models.Orders;

[DynamoDBTable(DynamoDbTableNames.RentalOrder)]
public sealed class RentalOrder
{
    [DynamoDBHashKey]
    [DynamoDBProperty("OrderId")]
    public required string OrderId { get; set; }

    /// <summary>Short human readable code used in emails and on the worker manifest.</summary>
    [DynamoDBProperty("OrderNumber")]
    public string OrderNumber { get; set; } = string.Empty;

    // The object persistence model can only route a query to a GSI when the index keys are declared
    // on the model, so every index attribute below is annotated rather than plain.
    [DynamoDBGlobalSecondaryIndexHashKey(new[]
    {
        DynamoDbTableNames.RentalOrderByRegionAndDeliveryDateIndex,
        DynamoDbTableNames.RentalOrderByRegionAndPickupDateIndex,
        DynamoDbTableNames.RentalOrderByRegionAndCreatedIndex
    })]
    [DynamoDBProperty("RegionId")]
    public string RegionId { get; set; } = string.Empty;

    [DynamoDBGlobalSecondaryIndexHashKey(DynamoDbTableNames.RentalOrderByUserIndex)]
    [DynamoDBProperty("UserId")]
    public string UserId { get; set; } = string.Empty;

    [DynamoDBProperty("Status")]
    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    [DynamoDBProperty("Source")]
    public OrderSource Source { get; set; } = OrderSource.Direct;

    // Contact
    [DynamoDBProperty("CustomerName")]
    public string CustomerName { get; set; } = string.Empty;

    [DynamoDBProperty("CustomerEmail")]
    public string CustomerEmail { get; set; } = string.Empty;

    [DynamoDBProperty("CustomerPhone")]
    public string CustomerPhone { get; set; } = string.Empty;

    // Logistics
    [DynamoDBProperty("DeliveryAddressLine1")]
    public string DeliveryAddressLine1 { get; set; } = string.Empty;

    [DynamoDBProperty("DeliveryAddressLine2")]
    public string? DeliveryAddressLine2 { get; set; }

    [DynamoDBProperty("DeliveryCity")]
    public string DeliveryCity { get; set; } = string.Empty;

    [DynamoDBProperty("DeliveryState")]
    public string DeliveryState { get; set; } = "AZ";

    [DynamoDBProperty("DeliveryZip")]
    public string DeliveryZip { get; set; } = string.Empty;

    [DynamoDBProperty("PickupAddressLine1")]
    public string PickupAddressLine1 { get; set; } = string.Empty;

    [DynamoDBProperty("PickupAddressLine2")]
    public string? PickupAddressLine2 { get; set; }

    [DynamoDBProperty("PickupCity")]
    public string PickupCity { get; set; } = string.Empty;

    [DynamoDBProperty("PickupState")]
    public string PickupState { get; set; } = "AZ";

    [DynamoDBProperty("PickupZip")]
    public string PickupZip { get; set; } = string.Empty;

    /// <summary>ISO date (yyyy-MM-dd). Doubles as the range key of the worker manifest index.</summary>
    [DynamoDBGlobalSecondaryIndexRangeKey(DynamoDbTableNames.RentalOrderByRegionAndDeliveryDateIndex)]
    [DynamoDBProperty("DeliveryDate")]
    public string DeliveryDate { get; set; } = string.Empty;

    [DynamoDBProperty("DeliveryWindow")]
    public string DeliveryWindow { get; set; } = string.Empty;

    [DynamoDBGlobalSecondaryIndexRangeKey(DynamoDbTableNames.RentalOrderByRegionAndPickupDateIndex)]
    [DynamoDBProperty("PickupDate")]
    public string PickupDate { get; set; } = string.Empty;

    [DynamoDBProperty("PickupWindow")]
    public string PickupWindow { get; set; } = string.Empty;

    [DynamoDBProperty("ZoneName")]
    public string ZoneName { get; set; } = string.Empty;

    // Package
    [DynamoDBProperty("PackageId")]
    public string PackageId { get; set; } = string.Empty;

    [DynamoDBProperty("PackageName")]
    public string PackageName { get; set; } = string.Empty;

    [DynamoDBProperty("CrateCount")]
    public int CrateCount { get; set; }

    [DynamoDBProperty("DollyCount")]
    public int DollyCount { get; set; }

    [DynamoDBProperty("RequiresIndexCard")]
    public bool RequiresIndexCard { get; set; } = true;

    [DynamoDBProperty("IndexCardIssuedAtUtc")]
    public DateTime? IndexCardIssuedAtUtc { get; set; }

    [DynamoDBProperty("RentalWeeks")]
    public int RentalWeeks { get; set; } = 1;

    // Money, all in cents
    [DynamoDBProperty("PackageBaseCents")]
    public int PackageBaseCents { get; set; }

    [DynamoDBProperty("ExtraWeeksCents")]
    public int ExtraWeeksCents { get; set; }

    [DynamoDBProperty("ZoneSurchargeCents")]
    public int ZoneSurchargeCents { get; set; }

    /// <summary>Zone the totes are collected from, when pickup is a different address than delivery.</summary>
    [DynamoDBProperty("PickupZoneName")]
    public string PickupZoneName { get; set; } = string.Empty;

    /// <summary>Extra trip surcharge when the pickup address falls in a different zone than delivery.</summary>
    [DynamoDBProperty("PickupZoneSurchargeCents")]
    public int PickupZoneSurchargeCents { get; set; }

    [DynamoDBProperty("AddOnsCents")]
    public int AddOnsCents { get; set; }

    [DynamoDBProperty("GiftCreditAppliedCents")]
    public int GiftCreditAppliedCents { get; set; }

    /// <summary>What the customer actually paid at checkout, after the gift credit.</summary>
    [DynamoDBProperty("TotalDueCents")]
    public int TotalDueCents { get; set; }

    /// <summary>Arizona TPT (and local) collected by Stripe Tax at checkout, based on delivery address.</summary>
    [DynamoDBProperty("TaxCents")]
    public int TaxCents { get; set; }

    [DynamoDBProperty("AmountPaidCents")]
    public int AmountPaidCents { get; set; }

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<AddOnLine>>))]
    public List<AddOnLine> AddOns { get; set; } = new();

    // Gift linkage
    [DynamoDBProperty("GiftId")]
    public string? GiftId { get; set; }

    [DynamoDBProperty("GiftingRealtorName")]
    public string? GiftingRealtorName { get; set; }

    [DynamoDBProperty("GiftingRealtorCompany")]
    public string? GiftingRealtorCompany { get; set; }

    /// <summary>Tells the operator to include the agent's co-branded insert in the delivery.</summary>
    [DynamoDBProperty("IncludeCoBrandingInsert")]
    public bool IncludeCoBrandingInsert { get; set; }

    // Stripe
    [DynamoDBProperty("StripeCustomerId")]
    public string? StripeCustomerId { get; set; }

    [DynamoDBProperty("StripeCheckoutSessionId")]
    public string? StripeCheckoutSessionId { get; set; }

    [DynamoDBProperty("StripePaymentIntentId")]
    public string? StripePaymentIntentId { get; set; }

    [DynamoDBProperty("PaymentMethodId")]
    public string? PaymentMethodId { get; set; }

    [DynamoDBProperty("CardBrand")]
    public string? CardBrand { get; set; }

    [DynamoDBProperty("CardLast4")]
    public string? CardLast4 { get; set; }

    // Embedded collections
    [DynamoDBProperty(typeof(JsonPropertyConverter<TermsAcceptance>))]
    public TermsAcceptance? Terms { get; set; }

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<OrderNote>>))]
    public List<OrderNote> Notes { get; set; } = new();

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<DamageLine>>))]
    public List<DamageLine> Damages { get; set; } = new();

    [DynamoDBProperty(typeof(JsonPropertyConverter<List<ExtensionCharge>>))]
    public List<ExtensionCharge> Extensions { get; set; } = new();

    // Fulfillment audit
    [DynamoDBProperty("ConfirmedAtUtc")]
    public DateTime? ConfirmedAtUtc { get; set; }

    [DynamoDBProperty("DeliveredAtUtc")]
    public DateTime? DeliveredAtUtc { get; set; }

    [DynamoDBProperty("PickedUpAtUtc")]
    public DateTime? PickedUpAtUtc { get; set; }

    [DynamoDBProperty("CratesReturned")]
    public int? CratesReturned { get; set; }

    [DynamoDBProperty("DolliesReturned")]
    public int? DolliesReturned { get; set; }

    [DynamoDBProperty("CancelledAtUtc")]
    public DateTime? CancelledAtUtc { get; set; }

    [DynamoDBProperty("CancellationReason")]
    public string? CancellationReason { get; set; }

    [DynamoDBGlobalSecondaryIndexRangeKey(new[]
    {
        DynamoDbTableNames.RentalOrderByUserIndex,
        DynamoDbTableNames.RentalOrderByRegionAndCreatedIndex
    })]
    [DynamoDBProperty("CreatedAtUtc")]
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBProperty("UpdatedAtUtc")]
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    [DynamoDBIgnore]
    public int SubtotalCents =>
        PackageBaseCents + ExtraWeeksCents + ZoneSurchargeCents + PickupZoneSurchargeCents + AddOnsCents;

    [DynamoDBIgnore]
    public int OutstandingDamageCents =>
        Damages.Where(d => d.Status == DamageChargeStatus.PendingReview).Sum(d => d.TotalCents);

    [DynamoDBIgnore]
    public bool IsActiveRental =>
        Status is OrderStatus.Confirmed or OrderStatus.OutForDelivery or OrderStatus.Delivered or OrderStatus.OutForPickup;
}
