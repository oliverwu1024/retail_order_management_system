using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>
/// Persistence for the customer profile + addresses (Story 1.4). Pure data access —
/// the "lazy create" and "one default per axis" business rules live in the service.
/// </summary>
public interface ICustomerProfileRepository
{
    /// <summary>The Identity user (tracked) — for the email + mirroring DisplayName + seeding a new profile.</summary>
    Task<ApplicationUser?> GetUserAsync(string appUserId, CancellationToken ct);

    /// <summary>The user's profile with its addresses, tracked (write path). Null if not yet created.</summary>
    Task<CustomerProfile?> GetProfileAsync(string appUserId, CancellationToken ct);

    /// <summary>The user's profile with its addresses, read-only (GET path). Null if not yet created.</summary>
    Task<CustomerProfile?> GetProfileReadOnlyAsync(string appUserId, CancellationToken ct);

    /// <summary>Stages a new profile for insert.</summary>
    Task AddProfileAsync(CustomerProfile profile, CancellationToken ct);

    /// <summary>
    /// An address by id, tracked, but ONLY if it belongs to the given user's profile.
    /// Returns null when the address doesn't exist OR isn't the caller's — both map to a
    /// 404 so we never reveal that someone else's address id exists.
    /// </summary>
    Task<Address?> GetOwnedAddressAsync(string appUserId, Guid addressId, CancellationToken ct);

    /// <summary>Stages a new address for insert.</summary>
    Task AddAddressAsync(Address address, CancellationToken ct);

    /// <summary>Stages an address for delete (hard delete — addresses aren't soft-deletable).</summary>
    void RemoveAddress(Address address);

    /// <summary>
    /// Clears <c>IsDefaultShipping</c> on the profile's addresses (optionally except one)
    /// via a single set-based UPDATE. Run before setting a new default so the filtered
    /// unique index never sees two defaults at once. Stamps the audit fields itself, since
    /// <c>ExecuteUpdate</c> bypasses the SaveChanges-based AuditingInterceptor.
    /// </summary>
    Task ClearDefaultShippingAsync(Guid profileId, Guid? exceptAddressId, DateTimeOffset updatedAt, string? updatedBy, CancellationToken ct);

    /// <summary>Billing twin of <see cref="ClearDefaultShippingAsync"/>.</summary>
    Task ClearDefaultBillingAsync(Guid profileId, Guid? exceptAddressId, DateTimeOffset updatedAt, string? updatedBy, CancellationToken ct);

    Task SaveChangesAsync(CancellationToken ct);
}
