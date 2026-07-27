using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using BoxDropAz.Core.Data;
using BoxDropAz.Web.Models.Identity;

namespace BoxDropAz.Web.Data;

public sealed class DynamoDbDataHelper
{
    private readonly IAmazonDynamoDB _client;

    public DynamoDbDataHelper(IAmazonDynamoDB client)
    {
        _client = client;
    }

    public IAmazonDynamoDB Client => _client;

    public IDynamoDBContext CreateContext()
    {
        return new DynamoDBContextBuilder()
            .WithDynamoDBClient(() => _client)
            .ConfigureContext(config =>
            {
                config.TableNamePrefix = DynamoDbTableNames.GetTablePrefix();
            })
            .Build();
    }

    public async Task<ApplicationUser?> GetUserByIdAsync(string userId, CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.LoadAsync<ApplicationUser>(userId, ct);
    }

    public async Task<ApplicationUser?> GetUserByAttributeAsync(string attributeName, string attributeValue, CancellationToken ct)
    {
        using var ctx = CreateContext();
        var results = await ctx.ScanAsync<ApplicationUser>(new List<ScanCondition>
        {
            new(attributeName, ScanOperator.Equal, attributeValue)
        }).GetRemainingAsync(ct);

        return results.FirstOrDefault();
    }

    public async Task<ApplicationRole?> GetRoleByIdAsync(string roleId, CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.LoadAsync<ApplicationRole>(roleId, ct);
    }

    public async Task<ApplicationRole?> GetRoleByNormalizedNameAsync(string normalizedName, CancellationToken ct)
        => await GetRoleByAttributeAsync("NormalizedName", normalizedName, ct);

    public async Task<ApplicationRole?> GetRoleByAttributeAsync(string attributeName, string attributeValue, CancellationToken ct)
    {
        using var ctx = CreateContext();
        var results = await ctx.ScanAsync<ApplicationRole>(new List<ScanCondition>
        {
            new(attributeName, ScanOperator.Equal, attributeValue)
        }).GetRemainingAsync(ct);

        return results.FirstOrDefault();
    }

    public async Task<List<ApplicationRole>> GetAllRolesAsync(CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.ScanAsync<ApplicationRole>(new List<ScanCondition>()).GetRemainingAsync(ct);
    }

    public async Task<List<string>> GetRoleIdsForUserAsync(string userId, CancellationToken ct)
    {
        using var ctx = CreateContext();
        var links = await ctx.QueryAsync<UserToRoles>(userId).GetRemainingAsync(ct);
        return links.Select(l => l.RoleId).ToList();
    }

    public async Task<List<ApplicationUser>> GetAllUsersAsync(CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.ScanAsync<ApplicationUser>(new List<ScanCondition>()).GetRemainingAsync(ct);
    }

    public async Task<List<ApplicationUser>> GetUsersInRegionAsync(string regionId, CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.ScanAsync<ApplicationUser>(new List<ScanCondition>
        {
            new("RegionId", ScanOperator.Equal, regionId)
        }).GetRemainingAsync(ct);
    }

    public async Task<UserLogin?> GetUserLoginAsync(string loginProvider, string providerKey, CancellationToken ct)
    {
        var id = UserLogin.CreateId(loginProvider, providerKey);
        using var ctx = CreateContext();
        return await ctx.LoadAsync<UserLogin>(id, ct);
    }

    public async Task<List<UserLogin>> GetUserLoginsAsync(string userId, CancellationToken ct)
    {
        using var ctx = CreateContext();
        return await ctx.QueryAsync<UserLogin>(userId, new QueryConfig
        {
            IndexName = DynamoDbTableNames.UserLoginByUserIndex
        }).GetRemainingAsync(ct);
    }
}
