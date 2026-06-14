using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Constants;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Identity;

/// <summary>
/// Idempotent startup seeder: guarantees the four RBAC roles exist and a default
/// administrator account is present (REQUIREMENTS §1.3). Safe to run on every boot
/// — it creates only what is missing.
/// </summary>
/// <remarks>
/// Invoked from <c>Program.cs</c> inside a DI scope after the host is built. The
/// admin password comes from <c>Auth:DefaultAdmin:Password</c> (User Secrets /
/// Key Vault); if it is absent the admin step is skipped with a warning rather
/// than seeding a guessable account.
/// </remarks>
public sealed class IdentityDataSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DefaultAdminOptions _adminOptions;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<DefaultAdminOptions> adminOptions,
        ILogger<IdentityDataSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _adminOptions = adminOptions.Value;
        _logger = logger;
    }

    /// <summary>Seeds roles then the default admin. Throws on a hard failure (fail fast at startup).</summary>
    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedDefaultAdminAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (string role in Roles.All)
        {
            if (await _roleManager.RoleExistsAsync(role))
            {
                continue;
            }

            IdentityResult result = await _roleManager.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to seed role '{role}': {DescribeErrors(result)}");
            }

            _logger.LogInformation("Seeded role {Role}", role);
        }
    }

    private async Task SeedDefaultAdminAsync()
    {
        if (string.IsNullOrWhiteSpace(_adminOptions.Email) || string.IsNullOrWhiteSpace(_adminOptions.Password))
        {
            _logger.LogWarning(
                "Default admin not seeded: Auth:DefaultAdmin Email/Password are not configured (set them via User Secrets / Key Vault).");
            return;
        }

        ApplicationUser? existing = await _userManager.FindByEmailAsync(_adminOptions.Email);
        if (existing is not null)
        {
            return;
        }

        var admin = new ApplicationUser
        {
            UserName = _adminOptions.Email,
            Email = _adminOptions.Email,
            DisplayName = _adminOptions.DisplayName,
            EmailConfirmed = true,
        };

        IdentityResult created = await _userManager.CreateAsync(admin, _adminOptions.Password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"Failed to seed default admin: {DescribeErrors(created)}");
        }

        IdentityResult roleResult = await _userManager.AddToRoleAsync(admin, Roles.Administrator);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to grant Administrator role: {DescribeErrors(roleResult)}");
        }

        // Note: the admin EMAIL is a sensitive field (CODING_STANDARDS) — log the id, not the email.
        _logger.LogInformation("Seeded default administrator account {UserId}", admin.Id);
    }

    private static string DescribeErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
}
