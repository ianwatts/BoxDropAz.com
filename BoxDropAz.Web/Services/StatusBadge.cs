using BoxDropAz.Core.Models.Orders;
using BoxDropAz.Core.Models.Realtors;

namespace BoxDropAz.Web.Services;

/// <summary>Maps statuses to badge classes and human labels so views stay free of switch blocks.</summary>
public static class StatusBadge
{
    public static string CssFor(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "bd-status bd-status-pending",
        OrderStatus.Confirmed => "bd-status bd-status-confirmed",
        OrderStatus.OutForDelivery or OrderStatus.OutForPickup => "bd-status bd-status-transit",
        OrderStatus.Delivered => "bd-status bd-status-delivered",
        OrderStatus.Completed => "bd-status bd-status-complete",
        OrderStatus.Cancelled => "bd-status bd-status-cancelled",
        _ => "bd-status bd-status-complete"
    };

    public static string LabelFor(OrderStatus status) => status switch
    {
        OrderStatus.PendingPayment => "Awaiting payment",
        OrderStatus.Confirmed => "Confirmed",
        OrderStatus.OutForDelivery => "Out for delivery",
        OrderStatus.Delivered => "Delivered",
        OrderStatus.OutForPickup => "Out for pickup",
        OrderStatus.Completed => "Completed",
        OrderStatus.Cancelled => "Cancelled",
        _ => status.ToString()
    };

    public static string CssFor(GiftStatus status) => status switch
    {
        GiftStatus.Sent => "bd-status bd-status-sent",
        GiftStatus.Claimed => "bd-status bd-status-claimed",
        GiftStatus.Cancelled => "bd-status bd-status-cancelled",
        GiftStatus.Expired => "bd-status bd-status-expired",
        _ => "bd-status bd-status-complete"
    };

    public static string LabelFor(GiftStatus status) => status switch
    {
        GiftStatus.Sent => "Awaiting claim",
        GiftStatus.Claimed => "Claimed",
        GiftStatus.Cancelled => "Cancelled",
        GiftStatus.Expired => "Expired",
        _ => status.ToString()
    };

    public static string CssFor(DamageChargeStatus status) => status switch
    {
        DamageChargeStatus.PendingReview => "bd-status bd-status-pending",
        DamageChargeStatus.Charged => "bd-status bd-status-delivered",
        DamageChargeStatus.Waived => "bd-status bd-status-complete",
        DamageChargeStatus.ChargeFailed => "bd-status bd-status-cancelled",
        _ => "bd-status bd-status-complete"
    };

    public static string LabelFor(DamageChargeStatus status) => status switch
    {
        DamageChargeStatus.PendingReview => "Pending review",
        DamageChargeStatus.Charged => "Charged",
        DamageChargeStatus.Waived => "Waived",
        DamageChargeStatus.ChargeFailed => "Charge failed",
        _ => status.ToString()
    };
}
