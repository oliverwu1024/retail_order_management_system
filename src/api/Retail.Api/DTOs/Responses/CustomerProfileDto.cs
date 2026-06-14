namespace Retail.Api.DTOs.Responses;

/// <summary>
/// The current customer's full profile for the "My Account" page (<c>GET /profile</c>).
/// <paramref name="Email"/> comes from the Identity user and is read-only (immutable in
/// the MVP); it's included here so the page can render it without a second call.
/// </summary>
public sealed record CustomerProfileDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? Phone,
    IReadOnlyList<AddressDto> Addresses);
