using Retail.Api.Common.Models;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>Back-office user administration (Phase 3 §10) — list + create Staff/StoreManager accounts.</summary>
public interface IAdminUserService
{
    /// <summary>Lists accounts, optionally filtered by role, paged.</summary>
    Task<PagedResult<AdminUserDto>> ListAsync(string? role, int page, int pageSize, CancellationToken ct);

    /// <summary>
    /// Creates a Staff or StoreManager account. Throws <see cref="Exceptions.ConflictException"/> if the
    /// email is taken or Identity rejects the account. The CALLER's authority (who may create which role)
    /// is enforced at the controller, not here.
    /// </summary>
    Task<AdminUserDto> CreateAsync(CreateUserRequest request, CancellationToken ct);
}
