using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Customer profile + address business logic (Story 1.4). Every method is scoped to a
/// single Identity user (<c>appUserId</c>) — a customer only ever acts on their own
/// data. Profiles are created lazily on first access. Throws <c>NotFoundException</c>
/// (→404) when an address isn't the caller's.
/// </summary>
public interface ICustomerProfileService
{
    Task<CustomerProfileDto> GetMyProfileAsync(string appUserId, CancellationToken ct);
    Task<CustomerProfileDto> UpdateMyProfileAsync(string appUserId, UpsertProfileRequest request, CancellationToken ct);

    Task<IReadOnlyList<AddressDto>> ListMyAddressesAsync(string appUserId, CancellationToken ct);
    Task<AddressDto> AddAddressAsync(string appUserId, AddressRequest request, CancellationToken ct);
    Task<AddressDto> UpdateAddressAsync(string appUserId, Guid addressId, AddressRequest request, CancellationToken ct);
    Task DeleteAddressAsync(string appUserId, Guid addressId, CancellationToken ct);
}
