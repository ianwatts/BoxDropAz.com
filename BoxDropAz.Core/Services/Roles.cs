namespace BoxDropAz.Core.Services;

public static class Roles
{
    /// <summary>Homebuyers and direct renters.</summary>
    public const string Customer = "Customer";

    /// <summary>Subscribed real estate agents who gift crates to closing clients.</summary>
    public const string Realtor = "Realtor";

    /// <summary>Drivers working the delivery and pickup manifest.</summary>
    public const string Worker = "Worker";

    /// <summary>Full control, but only over their own region.</summary>
    public const string RegionalAdmin = "RegionalAdmin";

    /// <summary>Full control across every region, plus region management.</summary>
    public const string SaaSAdmin = "SaaSAdmin";

    public static readonly string[] All =
    {
        Customer,
        Realtor,
        Worker,
        RegionalAdmin,
        SaaSAdmin
    };

    /// <summary>Roles allowed into the admin area. Regional admins are filtered by region downstream.</summary>
    public const string AnyAdmin = RegionalAdmin + "," + SaaSAdmin;
}
