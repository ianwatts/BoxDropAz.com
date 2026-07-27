using Amazon.DynamoDBv2.Model;
using BoxDropAz.Core.Services;
using BoxDropAz.Web.Models.Identity;
using Microsoft.AspNetCore.Identity;

namespace BoxDropAz.Web.Services;

public static class IdentitySeeder
{
    private sealed record SeedUser(string ConfigPrefix, string Role, string FullName, bool NeedsRegion);

    private static readonly SeedUser[] SeedUsers =
    {
        new("Admin", Roles.SaaSAdmin, "Platform Admin", false),
        new("RegionalAdmin", Roles.RegionalAdmin, "Phoenix Regional Admin", true),
        new("Worker", Roles.Worker, "Delivery Driver", true),
        new("Realtor", Roles.Realtor, "Demo Agent", true),
        new("Customer", Roles.Customer, "Demo Customer", true)
    };

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var roleManager = services.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var defaultRegionId = configuration.GetValue<string>("Seed:DefaultRegionId", "phoenix")!;

        try
        {
            foreach (var role in Roles.All)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new ApplicationRole
                    {
                        Name = role,
                        NormalizedName = role.ToUpperInvariant()
                    });
                }
            }

            foreach (var seed in SeedUsers)
            {
                await EnsureUserAsync(userManager, configuration, seed, defaultRegionId);
            }
        }
        catch (ResourceNotFoundException ex)
        {
            // Tables not created yet; the next boot with AutoCreateTables enabled will pick this up.
            Console.WriteLine($"Warning: identity seeding skipped, DynamoDB table missing: {ex.Message}");
        }
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        SeedUser seed,
        string defaultRegionId)
    {
        var email = Environment.GetEnvironmentVariable($"SEED_{seed.ConfigPrefix.ToUpperInvariant()}_EMAIL")
                    ?? configuration[$"Seed:{seed.ConfigPrefix}Email"];
        var password = Environment.GetEnvironmentVariable($"SEED_{seed.ConfigPrefix.ToUpperInvariant()}_PASSWORD")
                       ?? configuration[$"Seed:{seed.ConfigPrefix}Password"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Keep role membership correct even if the user predates a role being added.
            if (!await userManager.IsInRoleAsync(existing, seed.Role))
            {
                await userManager.AddToRoleAsync(existing, seed.Role);
            }
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = seed.FullName,
            RegionId = seed.NeedsRegion ? defaultRegionId : null
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, seed.Role);
            Console.WriteLine($"Seeded {seed.Role} user: {email}");
        }
        else
        {
            Console.WriteLine($"Warning: could not seed {seed.Role} user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
