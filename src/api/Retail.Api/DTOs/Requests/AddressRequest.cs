namespace Retail.Api.DTOs.Requests;

/// <summary>
/// Payload to create or update an address (DATABASE_DESIGN §3.3). The same shape
/// serves both <c>POST /profile/addresses</c> and <c>PUT /profile/addresses/{id}</c>
/// — there is no immutable field that would force a Create/Update split.
/// <paramref name="Country"/> is an ISO-3166 alpha-2 code.
/// </summary>
public sealed record AddressRequest(
    string Line1,
    string? Line2,
    string City,
    string? Region,
    string PostalCode,
    string Country,
    bool IsDefaultShipping,
    bool IsDefaultBilling);
