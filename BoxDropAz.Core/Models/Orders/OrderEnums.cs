namespace BoxDropAz.Core.Models.Orders;

public enum OrderStatus
{
    /// <summary>Created but Stripe has not confirmed payment yet. Not visible to workers.</summary>
    PendingPayment = 0,
    Confirmed = 1,
    OutForDelivery = 2,
    Delivered = 3,
    OutForPickup = 4,
    Completed = 5,
    Cancelled = 6
}

public enum OrderSource
{
    Direct = 0,
    RealtorGift = 1
}

public enum DamageChargeStatus
{
    /// <summary>Reported by a worker, waiting on admin review before the card is charged.</summary>
    PendingReview = 0,
    Charged = 1,
    Waived = 2,
    ChargeFailed = 3
}
