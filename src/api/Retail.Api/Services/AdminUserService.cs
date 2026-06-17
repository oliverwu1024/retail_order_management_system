using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Common.Models;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;

namespace Retail.Api.Services;

/// <summary>
/// Back-office user administration over ASP.NET Identity's <see cref="UserManager{TUser}"/>.
/// </summary>
/// <remarks>
/// Role authority (who may create whom) is a CONTROLLER concern — this service just performs the
/// requested create/list. New accounts are created <c>EmailConfirmed</c> (an admin vouches for
/// them), and the <c>AuditingInterceptor</c> stamps <c>CreatedBy</c> with the acting admin's id
/// automatically — and, since <c>ApplicationUser</c> is a monitored entity, the AuditTrailInterceptor
/// records an Insert row for the new account.
/// </remarks>
public sealed class AdminUserService : IAdminUserService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AdminUserService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AdminUserDto>> ListAsync(string? role, int page, int pageSize, CancellationToken ct)
    {
        int safePage = page < 1 ? 1 : page;
        int safeSize = Math.Clamp(pageSize, 1, 100);

        // A role filter goes through Identity's role lookup (no IQueryable for it); otherwise page
        // the user set directly. Materialise, then page in memory — fine at admin-account scale.
        IReadOnlyList<ApplicationUser> all = string.IsNullOrWhiteSpace(role)
            ? await _userManager.Users.OrderBy(u => u.Email).ToListAsync(ct)
            : (await _userManager.GetUsersInRoleAsync(role)).OrderBy(u => u.Email).ToList();

        int total = all.Count;
        IReadOnlyList<ApplicationUser> pageItems = all
            .Skip((safePage - 1) * safeSize)
            .Take(safeSize)
            .ToList();

        // GetRolesAsync per user is an N+1, acceptable for a handful of back-office accounts; a join
        // over AspNetUserRoles is the optimisation if the user table ever grows large.
        var dtos = new List<AdminUserDto>(pageItems.Count);
        foreach (ApplicationUser user in pageItems)
        {
            IList<string> roles = await _userManager.GetRolesAsync(user);
            dtos.Add(new AdminUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, roles.ToList()));
        }

        return new PagedResult<AdminUserDto>(dtos, total, safePage, safeSize);
    }

    /// <inheritdoc />
    public async Task<AdminUserDto> CreateAsync(CreateUserRequest request, CancellationToken ct)
    {
        // Pre-check for a friendly 409; Identity's own unique-index is the race-proof backstop.
        if (await _userManager.FindByEmailAsync(request.Email) is not null)
        {
            throw new ConflictException($"An account with email '{request.Email}' already exists.");
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            DisplayName = request.DisplayName,
            EmailConfirmed = true, // an admin is vouching for this account
        };

        IdentityResult created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            throw new ConflictException(
                "Could not create the account: " + string.Join("; ", created.Errors.Select(e => e.Description)));
        }

        IdentityResult roleResult = await _userManager.AddToRoleAsync(user, request.Role);
        if (!roleResult.Succeeded)
        {
            throw new ConflictException(
                "Could not assign the role: " + string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        return new AdminUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, new[] { request.Role });
    }
}
