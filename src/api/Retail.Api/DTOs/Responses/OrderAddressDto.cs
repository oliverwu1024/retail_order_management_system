namespace Retail.Api.DTOs.Responses;

/// <summary>An order's address snapshot as returned to the customer (Story 2.4).</summary>
public sealed record OrderAddressDto(
    string? RecipientName,
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country);
