using Microsoft.Extensions.Configuration;

namespace BoxDropAz.Core.Data;

public static class DynamoDbTableNames
{
    // Identity
    public const string ApplicationUser = "ApplicationUser";
    public const string ApplicationRole = "ApplicationRole";
    public const string UserToRoles = "UserToRoles";
    public const string UserLogin = "UserLogin";

    // Catalog and geography
    public const string Region = "Region";
    public const string CratePackage = "CratePackage";

    // Orders and fulfillment
    public const string RentalOrder = "RentalOrder";

    // Realtor gifting
    public const string RealtorSubscription = "RealtorSubscription";
    public const string GiftOrder = "GiftOrder";
    public const string CreditLedger = "CreditLedger";

    // Billing plumbing
    public const string StripeEvent = "StripeEvent";

    // Index names
    public const string RentalOrderByRegionAndDeliveryDateIndex = "RegionId-DeliveryDate-index";
    public const string RentalOrderByRegionAndPickupDateIndex = "RegionId-PickupDate-index";
    public const string RentalOrderByUserIndex = "UserId-CreatedAtUtc-index";
    public const string RentalOrderByRegionAndCreatedIndex = "RegionId-CreatedAtUtc-index";
    public const string GiftOrderByRealtorIndex = "RealtorUserId-CreatedAtUtc-index";
    public const string GiftOrderByClaimTokenIndex = "ClaimToken-index";
    public const string UserLoginByUserIndex = "UserId-index";

    private static string _tablePrefix = "";

    public static void Initialize(IConfiguration configuration)
    {
        _tablePrefix = configuration.GetValue<string>("DynamoDB:TablePrefix", "BoxDropAz_Dev_")!;
        Console.WriteLine($"DynamoDbTableNames initialized with prefix: '{_tablePrefix}'");
    }

    public static void SetTablePrefix(string prefix)
    {
        _tablePrefix = prefix ?? "";
        Console.WriteLine($"DynamoDbTableNames prefix set directly to: '{_tablePrefix}'");
    }

    public static string GetTablePrefix() => _tablePrefix;

    public static string GetTableName(string baseName)
        => string.IsNullOrEmpty(_tablePrefix) ? baseName : $"{_tablePrefix}{baseName}";
}
