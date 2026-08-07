namespace BoxDropAz.Core.Models.Regions;

/// <summary>
/// Per-region staff email routing. Each notification type can target roles and/or specific users.
/// </summary>
public sealed class RegionNotificationSettings
{
    public List<NotificationSubscription> Subscriptions { get; set; } = new();

    public NotificationSubscription For(string type)
    {
        var match = Subscriptions.FirstOrDefault(s =>
            string.Equals(s.Type, type, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return match;
        }

        match = new NotificationSubscription { Type = type };
        Subscriptions.Add(match);
        return match;
    }

    public static RegionNotificationSettings CreateDefaults() => new()
    {
        Subscriptions =
        [
            new NotificationSubscription
            {
                Type = NotificationTypes.NewBooking,
                NotifySaaSAdmin = true,
                NotifyRegionalAdmin = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.OrderStatusChanged,
                NotifyRegionalAdmin = true,
                NotifyWorker = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.OrderCancelled,
                NotifySaaSAdmin = true,
                NotifyRegionalAdmin = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.InventoryRestock,
                NotifyRegionalAdmin = true,
                NotifyWorker = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.DamagePending,
                NotifyRegionalAdmin = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.DamageChargeFailed,
                NotifySaaSAdmin = true,
                NotifyRegionalAdmin = true
            },
            new NotificationSubscription
            {
                Type = NotificationTypes.ContactForm,
                NotifySaaSAdmin = true,
                NotifyRegionalAdmin = true
            }
        ]
    };
}

public sealed class NotificationSubscription
{
    public string Type { get; set; } = string.Empty;

    public bool NotifySaaSAdmin { get; set; }

    public bool NotifyRegionalAdmin { get; set; }

    public bool NotifyWorker { get; set; }

    /// <summary>Additional staff user ids who should receive this alert regardless of role toggles.</summary>
    public List<string> ExtraUserIds { get; set; } = new();

    public bool HasAnyTarget =>
        NotifySaaSAdmin || NotifyRegionalAdmin || NotifyWorker || ExtraUserIds.Count > 0;
}

public static class NotificationTypes
{
    public const string NewBooking = "NewBooking";
    public const string OrderStatusChanged = "OrderStatusChanged";
    public const string OrderCancelled = "OrderCancelled";
    public const string InventoryRestock = "InventoryRestock";
    public const string DamagePending = "DamagePending";
    public const string DamageChargeFailed = "DamageChargeFailed";
    public const string ContactForm = "ContactForm";

    public static readonly IReadOnlyList<NotificationTypeInfo> All =
    [
        new(NewBooking, "New booking", "When a rental is paid and confirmed."),
        new(OrderStatusChanged, "Pickup / drop-off updates", "When delivery or pickup status changes."),
        new(OrderCancelled, "Order cancelled", "When a customer or admin cancels a rental."),
        new(InventoryRestock, "Inventory restock needed", "When a new restock task is opened for the region."),
        new(DamagePending, "Damage pending review", "When missing or damaged equipment is reported."),
        new(DamageChargeFailed, "Damage charge failed", "When charging a card for damages fails."),
        new(ContactForm, "Contact form", "Website enquiry submissions.")
    ];
}

public sealed record NotificationTypeInfo(string Type, string Label, string Description);
