using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.DocumentModel;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Data;

public sealed class UserStore :
    IUserStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>,
    IUserRoleStore<ApplicationUser>,
    IUserLoginStore<ApplicationUser>
{
    private readonly DynamoDbDataHelper _data;

    public UserStore(DynamoDbDataHelper data)
    {
        _data = data;
    }

    // IUserStore

    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.Id))
        {
            user.Id = Guid.NewGuid().ToString();
        }

        user.NormalizedUserName ??= user.UserName?.ToUpperInvariant();
        user.NormalizedEmail ??= user.Email?.ToUpperInvariant();

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync(user, cancellationToken);
        return IdentityResult.Success;
    }

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        => _data.GetUserByIdAsync(userId, cancellationToken);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        => _data.GetUserByAttributeAsync("NormalizedUserName", normalizedUserName, cancellationToken);

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Id);

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    // IUserEmailStore

    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.Email);

    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    {
        user.Email = email;
        return Task.CompletedTask;
    }

    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.EmailConfirmed);

    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.EmailConfirmed = confirmed;
        return Task.CompletedTask;
    }

    public Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        => _data.GetUserByAttributeAsync("NormalizedEmail", normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.NormalizedEmail);

    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    {
        user.NormalizedEmail = normalizedEmail;
        return Task.CompletedTask;
    }

    // IUserPasswordStore

    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PasswordHash);

    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash;
        return Task.CompletedTask;
    }

    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    // IUserPhoneNumberStore

    public Task<string?> GetPhoneNumberAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PhoneNumber);

    public Task SetPhoneNumberAsync(ApplicationUser user, string? phoneNumber, CancellationToken cancellationToken)
    {
        user.PhoneNumber = phoneNumber;
        return Task.CompletedTask;
    }

    public Task<bool> GetPhoneNumberConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
        => Task.FromResult(user.PhoneNumberConfirmed);

    public Task SetPhoneNumberConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    {
        user.PhoneNumberConfirmed = confirmed;
        return Task.CompletedTask;
    }

    // IUserRoleStore

    public async Task AddToRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await _data.GetRoleByNormalizedNameAsync(roleName.ToUpperInvariant(), cancellationToken);
        if (role is null)
        {
            return;
        }

        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(new UserToRoles { UserId = user.Id, RoleId = role.Id }, cancellationToken);
    }

    public async Task RemoveFromRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        var role = await _data.GetRoleByNormalizedNameAsync(roleName.ToUpperInvariant(), cancellationToken);
        if (role is null)
        {
            return;
        }

        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync<UserToRoles>(user.Id, role.Id, cancellationToken);
    }

    public async Task<IList<string>> GetRolesAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var roleIds = await _data.GetRoleIdsForUserAsync(user.Id, cancellationToken);
        if (roleIds.Count == 0)
        {
            return new List<string>();
        }

        var allRoles = await _data.GetAllRolesAsync(cancellationToken);
        return allRoles
            .Where(r => roleIds.Contains(r.Id))
            .Select(r => r.Name ?? string.Empty)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    public async Task<bool> IsInRoleAsync(ApplicationUser user, string roleName, CancellationToken cancellationToken)
    {
        var roles = await GetRolesAsync(user, cancellationToken);
        return roles.Contains(roleName, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IList<ApplicationUser>> GetUsersInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var role = await _data.GetRoleByNormalizedNameAsync(roleName.ToUpperInvariant(), cancellationToken);
        if (role is null)
        {
            return new List<ApplicationUser>();
        }

        using var ctx = _data.CreateContext();
        var links = await ctx.ScanAsync<UserToRoles>(new List<ScanCondition>
        {
            new("RoleId", ScanOperator.Equal, role.Id)
        }).GetRemainingAsync(cancellationToken);

        var users = new List<ApplicationUser>();
        foreach (var link in links)
        {
            var user = await _data.GetUserByIdAsync(link.UserId, cancellationToken);
            if (user is not null)
            {
                users.Add(user);
            }
        }

        return users;
    }

    // IUserLoginStore

    public async Task AddLoginAsync(ApplicationUser user, UserLoginInfo login, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.SaveAsync(new UserLogin
        {
            Id = UserLogin.CreateId(login.LoginProvider, login.ProviderKey),
            LoginProvider = login.LoginProvider,
            ProviderKey = login.ProviderKey,
            ProviderDisplayName = login.ProviderDisplayName,
            UserId = user.Id
        }, cancellationToken);
    }

    public async Task RemoveLoginAsync(ApplicationUser user, string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        using var ctx = _data.CreateContext();
        await ctx.DeleteAsync<UserLogin>(UserLogin.CreateId(loginProvider, providerKey), cancellationToken);
    }

    public async Task<IList<UserLoginInfo>> GetLoginsAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var logins = await _data.GetUserLoginsAsync(user.Id, cancellationToken);
        return logins
            .Select(l => new UserLoginInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))
            .ToList();
    }

    public async Task<ApplicationUser?> FindByLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
    {
        var login = await _data.GetUserLoginAsync(loginProvider, providerKey, cancellationToken);
        if (login is null)
        {
            return null;
        }

        return await _data.GetUserByIdAsync(login.UserId, cancellationToken);
    }

    public void Dispose()
    {
    }
}
