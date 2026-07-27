using Amazon.DynamoDBv2.DataModel;
using BoxDropAz.Core.Data;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Models.Identity;

[DynamoDBTable(DynamoDbTableNames.ApplicationRole)]
public class ApplicationRole : IdentityRole
{
    [DynamoDBHashKey]
    [DynamoDBProperty("Id")]
    public override string Id { get; set; } = Guid.NewGuid().ToString("N");

    [DynamoDBProperty("Name")]
    public override string? Name { get; set; }

    [DynamoDBProperty("NormalizedName")]
    public override string? NormalizedName { get; set; }
}
