using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Data;

public sealed class RoleStore : IRoleStore<ApplicationRole>
{
    private readonly DynamoDbDataHelper _data;

    public RoleStore(DynamoDbDataHelper data)
    {
        _data = data;
    }

    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(role.Id))
        {
            role.Id = Guid.NewGuid().ToString("N");
        }

        role.NormalizedName ??= role.Name?.ToUpperInvariant();

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(role, cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(role, cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync(role, cancellationToken);
        return IdentityResult.Success;
    }

    public Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
        => _data.GetRoleByIdAsync(roleId, cancellationToken);

    public Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
        => _data.GetRoleByNormalizedNameAsync(normalizedRoleName, cancellationToken);

    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.Id);

    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.Name);

    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
    {
        role.Name = roleName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
        => Task.FromResult(role.NormalizedName);

    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
    {
        role.NormalizedName = normalizedName;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}
