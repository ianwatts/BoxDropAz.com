using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Web.Models.Identity;

[DynamoDBTable(DynamoDbTableNames.UserLogin)]
public sealed class UserLogin
{
    [DynamoDBHashKey]
    [DynamoDBProperty("Id")]
    public required string Id { get; set; }

    [DynamoDBProperty("LoginProvider")]
    public string LoginProvider { get; set; } = string.Empty;

    [DynamoDBProperty("ProviderKey")]
    public string ProviderKey { get; set; } = string.Empty;

    [DynamoDBProperty("ProviderDisplayName")]
    public string? ProviderDisplayName { get; set; }

    // Declared as the index key so "all external logins for this user" can be a query.
    [DynamoDBGlobalSecondaryIndexHashKey(DynamoDbTableNames.UserLoginByUserIndex)]
    [DynamoDBProperty("UserId")]
    public string UserId { get; set; } = string.Empty;

    public static string CreateId(string loginProvider, string providerKey)
        => $"{loginProvider}#{providerKey}";
}
