using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Mappers;

/// <summary>
/// Explicit entity → DTO mapping for the customer profile (no AutoMapper — CODING_STANDARDS).
/// Extension methods so call sites read as <c>profile.ToDto(email)</c>.
/// </summary>
public static class CustomerProfileMappers
{
    public static AddressDto ToDto(this Address address) =>
        new(
            address.Id,
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.Country,
            address.IsDefaultShipping,
            address.IsDefaultBilling);

    /// <summary>
    /// Maps a profile + the owning user's email to the response DTO. Email is threaded
    /// in separately because it lives on the Identity user, not the profile. Addresses
    /// are ordered defaults-first for a stable, useful display order.
    /// </summary>
    public static CustomerProfileDto ToDto(this CustomerProfile profile, string email) =>
        new(
            profile.Id,
            email,
            profile.DisplayName,
            profile.Phone,
            profile.Addresses
                .OrderByDescending(a => a.IsDefaultShipping)
                .ThenByDescending(a => a.IsDefaultBilling)
                .ThenBy(a => a.CreatedAt)
                .Select(a => a.ToDto())
                .ToList());
}
