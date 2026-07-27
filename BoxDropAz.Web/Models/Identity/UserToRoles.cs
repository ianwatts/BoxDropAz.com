using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;

namespace BoxDropAz.Web.Models.Identity;

[DynamoDBTable(DynamoDbTableNames.UserToRoles)]
public sealed class UserToRoles
{
    [DynamoDBHashKey]
    [DynamoDBProperty("UserId")]
    public required string UserId { get; set; }

    [DynamoDBRangeKey]
    [DynamoDBProperty("RoleId")]
    public required string RoleId { get; set; }
}
