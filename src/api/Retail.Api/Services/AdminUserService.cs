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
/// automatically. NOTE: account rows are deliberately NOT in the AuditTrailInterceptor's monitored
/// set (REQUIREMENTS §11.1 lists Product/Inventory/Order/Payment/Shipment), so no before/after audit
/// row is written for user CRUD — which also avoids serialising Identity's email/normalized columns.
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

        int total;
        IReadOnlyList<ApplicationUser> pageItems;
        if (string.IsNullOrWhiteSpace(role))
        {
            // No role filter → page in SQL (COUNT + OFFSET/FETCH) so we never materialise the whole
            // user table just to return one page.
            IQueryable<ApplicationUser> ordered = _userManager.Users.OrderBy(u => u.Email);
            total = await ordered.CountAsync(ct);
            pageItems = await ordered.Skip((safePage - 1) * safeSize).Take(safeSize).ToListAsync(ct);
        }
        else
        {
            // Identity's role lookup has no IQueryable/paged variant, so it returns everyone in the
            // role; page that (smaller, role-bounded) set in memory.
            IReadOnlyList<ApplicationUser> inRole = (await _userManager.GetUsersInRoleAsync(role))
                .OrderBy(u => u.Email).ToList();
            total = inRole.Count;
            pageItems = inRole.Skip((safePage - 1) * safeSize).Take(safeSize).ToList();
        }

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
