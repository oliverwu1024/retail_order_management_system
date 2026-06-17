namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Admin request to create a back-office account (Phase 3 §10). <see cref="Role"/> is restricted
/// to <c>Staff</c> or <c>StoreManager</c> by the validator; who may create <em>which</em> role is
/// enforced at the controller (StoreManager creation is Administrator-only).
/// </summary>
public sealed record CreateUserRequest(string Email, string Password, string DisplayName, string Role);
