using Microsoft.EntityFrameworkCore;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Repositories;

/// <summary>EF Core implementation of <see cref="ICustomerProfileRepository"/>.</summary>
public sealed class CustomerProfileRepository : ICustomerProfileRepository
{
    private readonly RetailDbContext _db;

    public CustomerProfileRepository(RetailDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> GetUserAsync(string appUserId, CancellationToken ct) =>
        await _db.Users.FirstOrDefaultAsync(u => u.Id == appUserId, ct);

    /// <inheritdoc />
    public async Task<CustomerProfile?> GetProfileAsync(string appUserId, CancellationToken ct) =>
        await _db.CustomerProfiles
            .Include(p => p.Addresses)
            .Include(p => p.User) // for the email + DisplayName mirror (tracked)
            .FirstOrDefaultAsync(p => p.AppUserId == appUserId, ct);

    /// <inheritdoc />
    public async Task<CustomerProfile?> GetProfileReadOnlyAsync(string appUserId, CancellationToken ct) =>
        await _db.CustomerProfiles
            .AsNoTracking()
            .Include(p => p.Addresses)
            .Include(p => p.User) // for the email in the response DTO
            .FirstOrDefaultAsync(p => p.AppUserId == appUserId, ct);

    /// <inheritdoc />
    public async Task AddProfileAsync(CustomerProfile profile, CancellationToken ct) =>
        await _db.CustomerProfiles.AddAsync(profile, ct);

    /// <inheritdoc />
    public async Task<Address?> GetOwnedAddressAsync(string appUserId, Guid addressId, CancellationToken ct) =>
        await _db.Addresses
            .Include(a => a.CustomerProfile)
            .FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerProfile!.AppUserId == appUserId, ct);

    /// <inheritdoc />
    public async Task AddAddressAsync(Address address, CancellationToken ct) =>
        await _db.Addresses.AddAsync(address, ct);

    /// <inheritdoc />
    public void RemoveAddress(Address address) =>
        _db.Addresses.Remove(address);

    /// <inheritdoc />
    public async Task ClearDefaultShippingAsync(Guid profileId, Guid? exceptAddressId, CancellationToken ct)
    {
        IQueryable<Address> query = _db.Addresses
            .Where(a => a.CustomerProfileId == profileId && a.IsDefaultShipping);

        if (exceptAddressId is Guid except)
        {
            query = query.Where(a => a.Id != except);
        }

        // Set-based UPDATE issued immediately (participates in the ambient transaction).
        await query.ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefaultShipping, false), ct);
    }

    /// <inheritdoc />
    public async Task ClearDefaultBillingAsync(Guid profileId, Guid? exceptAddressId, CancellationToken ct)
    {
        IQueryable<Address> query = _db.Addresses
            .Where(a => a.CustomerProfileId == profileId && a.IsDefaultBilling);

        if (exceptAddressId is Guid except)
        {
            query = query.Where(a => a.Id != except);
        }

        await query.ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefaultBilling, false), ct);
    }

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct) =>
        await _db.SaveChangesAsync(ct);
}
