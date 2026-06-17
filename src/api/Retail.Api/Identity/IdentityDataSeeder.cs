using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Retail.Api.Common.Constants;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Identity;

/// <summary>
/// Idempotent startup seeder: guarantees the four RBAC roles exist, a default administrator
/// account is present (REQUIREMENTS §1.3), and — OUTSIDE production — the demo Staff/StoreManager
/// accounts the multi-role admin demo logs into. Safe to run on every boot; it creates only what
/// is missing.
/// </summary>
/// <remarks>
/// Invoked from <c>Program.cs</c> inside a DI scope after the host is built. The admin password
/// comes from <c>Auth:DefaultAdmin:Password</c> (User Secrets / Key Vault); if it is absent the
/// admin step is skipped with a warning rather than seeding a guessable account. The demo accounts
/// come from <c>Auth:DemoStaff</c>/<c>Auth:DemoManager</c> and are NEVER seeded in Production — a
/// belt-and-braces environment guard on top of "skip if the credentials are unset".
/// </remarks>
public sealed class IdentityDataSeeder
{
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DefaultAdminOptions _adminOptions;
    private readonly IConfiguration _config;
    private readonly IHostEnvironment _env;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<DefaultAdminOptions> adminOptions,
        IConfiguration config,
        IHostEnvironment env,
        ILogger<IdentityDataSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _adminOptions = adminOptions.Value;
        _config = config;
        _env = env;
        _logger = logger;
    }

    /// <summary>Seeds roles, the default admin, and (non-production) the demo accounts. Throws on a hard failure (fail fast at startup).</summary>
    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedDefaultAdminAsync();

        // The demo Staff/StoreManager accounts exist only to make the "three roles, three sidebars"
        // demo reproducible — never in Production, regardless of what config is present.
        if (!_env.IsProduction())
        {
            await SeedDemoAccountAsync("Auth:DemoStaff", Roles.Staff);
            await SeedDemoAccountAsync("Auth:DemoManager", Roles.StoreManager);
        }
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

    /// <summary>
    /// Seeds one demo back-office account from a config section (<c>{section}:Email/Password/DisplayName</c>)
    /// and grants it <paramref name="role"/>. Skipped if Email/Password aren't configured. Caller has
    /// already gated this to non-production.
    /// </summary>
    private async Task SeedDemoAccountAsync(string section, string role)
    {
        string? email = _config[$"{section}:Email"];
        string? password = _config[$"{section}:Password"];
        string? displayName = _config[$"{section}:DisplayName"];

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogDebug("Demo {Role} account not seeded: {Section} Email/Password not configured.", role, section);
            return;
        }

        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true,
        };

        IdentityResult created = await _userManager.CreateAsync(user, password);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException($"Failed to seed demo {role} account: {DescribeErrors(created)}");
        }

        IdentityResult roleResult = await _userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException($"Failed to grant {role} role to demo account: {DescribeErrors(roleResult)}");
        }

        _logger.LogInformation("Seeded demo {Role} account {UserId} (non-production).", role, user.Id);
    }

    private static string DescribeErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => $"{e.Code}:{e.Description}"));
}
