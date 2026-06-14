namespace Retail.Api.DTOs.Responses;

/// <summary>An address as returned to its owner (DATABASE_DESIGN §3.3).</summary>
public sealed record AddressDto(
    Guid Id,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country,
    bool IsDefaultShipping,
    bool IsDefaultBilling);
