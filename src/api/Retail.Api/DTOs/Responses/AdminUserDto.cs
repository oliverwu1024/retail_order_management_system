namespace Retail.Api.DTOs.Responses;

/// <summary>An account as shown in the admin user-management list (Phase 3 §10).</summary>
public sealed record AdminUserDto(
    string Id,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles);
