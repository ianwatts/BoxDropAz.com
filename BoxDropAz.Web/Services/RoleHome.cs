using System.Security.Claims;
using BoxDropAz.Core.Services;

namespace BoxDropAz.Web.Services;

/// <summary>
/// Each role has a different home screen, so sign-in and "go to my dashboard" links resolve
/// through here rather than defaulting everyone to the customer dashboard.
/// </summary>
public static class RoleHome
{
    public static string ForRoles(IEnumerable<string> roles)
    {
        var set = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);

        if (set.Contains(Roles.SaaSAdmin)) return "/SaaSAdmin";
        if (set.Contains(Roles.RegionalAdmin)) return "/Admin";
        if (set.Contains(Roles.Worker)) return "/Worker";
        if (set.Contains(Roles.Realtor)) return "/Agent";
        return "/Dashboard";
    }

    public static string ForPrincipal(ClaimsPrincipal principal)
    {
        var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);
        return ForRoles(roles);
    }
}
