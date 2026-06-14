using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Customer profile + address business logic (Story 1.4).
/// </summary>
/// <remarks>
/// <para>
/// PII RULE (CODING_STANDARDS §日志): Email, phone, and address fields must NEVER enter
/// a log or an exception message (exception messages are surfaced to clients and, in
/// Development, echoed with the stack trace). Every log line here uses only the
/// blessed-safe identifiers — the user id (Guid) and the profile/address id.
/// </para>
/// <para>
/// DEFAULT-ADDRESS INVARIANT: "at most one default shipping + one default billing per
/// profile" is guaranteed by filtered unique indexes (AddressConfiguration). To set a
/// new default without the index tripping mid-write, the prior default is cleared with a
/// set-based UPDATE first, then the target is written — both inside one transaction so
/// the change is atomic.
/// </para>
/// </remarks>
public sealed class CustomerProfileService : ICustomerProfileService
{
    private readonly ICustomerProfileRepository _repo;
    private readonly RetailDbContext _db; // for transactions only (CODING_STANDARDS-sanctioned)
    private readonly ILogger<CustomerProfileService> _logger;

    public CustomerProfileService(
        ICustomerProfileRepository repo,
        RetailDbContext db,
        ILogger<CustomerProfileService> logger)
    {
        _repo = repo;
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CustomerProfileDto> GetMyProfileAsync(string appUserId, CancellationToken ct)
    {
        CustomerProfile? existing = await _repo.GetProfileReadOnlyAsync(appUserId, ct);
        if (existing is not null)
        {
            return existing.ToDto(existing.User?.Email ?? string.Empty);
        }

        // No profile yet → lazily create one, seeded from the registration display name.
        // Absence is already proven, so create directly (skips a redundant tracked read).
        CustomerProfile created = await CreateProfileAsync(appUserId, ct);
        return created.ToDto(created.User?.Email ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<CustomerProfileDto> UpdateMyProfileAsync(string appUserId, UpsertProfileRequest request, CancellationToken ct)
    {
        CustomerProfile profile = await GetOrCreateProfileAsync(appUserId, ct);

        profile.DisplayName = request.DisplayName.Trim();
        profile.Phone = Trimmed(request.Phone);

        // Mirror DisplayName onto the Identity user so the lightweight /auth/me + the SPA
        // session keep showing the current name without ever loading the profile.
        if (profile.User is not null)
        {
            profile.User.DisplayName = profile.DisplayName;
        }

        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation("Profile {ProfileId} updated for user {UserId}.", profile.Id, appUserId);
        return profile.ToDto(profile.User?.Email ?? string.Empty);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AddressDto>> ListMyAddressesAsync(string appUserId, CancellationToken ct)
    {
        CustomerProfile? profile = await _repo.GetProfileReadOnlyAsync(appUserId, ct);
        if (profile is null)
        {
            return Array.Empty<AddressDto>();
        }

        return profile.ToDto(profile.User?.Email ?? string.Empty).Addresses;
    }

    /// <inheritdoc />
    public async Task<AddressDto> AddAddressAsync(string appUserId, AddressRequest request, CancellationToken ct)
    {
        CustomerProfile profile = await GetOrCreateProfileAsync(appUserId, ct);

        var address = new Address { CustomerProfileId = profile.Id };
        ApplyAddressFields(address, request);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ClearSupersededDefaultsAsync(profile.Id, exceptAddressId: null, request, ct);
        await _repo.AddAddressAsync(address, ct);
        await SaveDefaultChangeAsync(tx, ct);

        _logger.LogInformation("Address {AddressId} added to profile {ProfileId}.", address.Id, profile.Id);
        return address.ToDto();
    }

    /// <inheritdoc />
    public async Task<AddressDto> UpdateAddressAsync(string appUserId, Guid addressId, AddressRequest request, CancellationToken ct)
    {
        // Scoped to the caller — null when the address is missing OR belongs to someone
        // else, both surfacing as a 404 so we never confirm another user's address id.
        Address address = await _repo.GetOwnedAddressAsync(appUserId, addressId, ct)
            ?? throw new NotFoundException($"Address '{addressId}' was not found.");

        ApplyAddressFields(address, request);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ClearSupersededDefaultsAsync(address.CustomerProfileId, exceptAddressId: addressId, request, ct);
        await SaveDefaultChangeAsync(tx, ct);

        _logger.LogInformation("Address {AddressId} updated.", addressId);
        return address.ToDto();
    }

    /// <inheritdoc />
    public async Task DeleteAddressAsync(string appUserId, Guid addressId, CancellationToken ct)
    {
        Address address = await _repo.GetOwnedAddressAsync(appUserId, addressId, ct)
            ?? throw new NotFoundException($"Address '{addressId}' was not found.");

        _repo.RemoveAddress(address);
        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation("Address {AddressId} deleted.", addressId);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // Loads the caller's profile (with addresses + user), creating it on first access.
    private async Task<CustomerProfile> GetOrCreateProfileAsync(string appUserId, CancellationToken ct) =>
        await _repo.GetProfileAsync(appUserId, ct) ?? await CreateProfileAsync(appUserId, ct);

    // Creates the profile, tolerating a concurrent first-access that inserted it first:
    // the unique index on AppUserId rejects the duplicate, so we detach our losing entity
    // and return the row the other request committed. This makes lazy creation idempotent
    // instead of letting the loser surface as a 500. If the failure ISN'T that race (no row
    // appeared), it's rethrown rather than swallowed.
    private async Task<CustomerProfile> CreateProfileAsync(string appUserId, CancellationToken ct)
    {
        ApplicationUser user = await _repo.GetUserAsync(appUserId, ct)
            ?? throw new NotFoundException("The current user no longer exists.");

        var profile = new CustomerProfile
        {
            AppUserId = appUserId,
            DisplayName = user.DisplayName ?? user.Email ?? string.Empty,
            User = user, // link the tracked user so email + the DisplayName mirror work
        };

        await _repo.AddProfileAsync(profile, ct);
        try
        {
            await _repo.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(profile).State = EntityState.Detached;
            CustomerProfile? winner = await _repo.GetProfileAsync(appUserId, ct);
            if (winner is not null)
            {
                return winner;
            }

            throw;
        }

        _logger.LogInformation("Created customer profile {ProfileId} for user {UserId}.", profile.Id, appUserId);
        return profile;
    }

    // Persists a default-address change inside its transaction, translating the filtered-
    // unique-index collision that a CONCURRENT same-axis default-set produces into the
    // project-standard 409 rather than a raw 500. (The clear-then-set ordering already
    // prevents a single request from tripping the index; this guards two racing requests.)
    // SQL Server raises error 2601/2627 on a unique-index violation.
    private async Task SaveDefaultChangeAsync(IDbContextTransaction tx, CancellationToken ct)
    {
        try
        {
            await _repo.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("Another request just changed your default address. Please refresh and try again.");
        }
    }

    // Clears the prior default(s) the incoming request is about to supersede, so the
    // filtered unique index never sees two defaults for the same axis.
    private async Task ClearSupersededDefaultsAsync(Guid profileId, Guid? exceptAddressId, AddressRequest request, CancellationToken ct)
    {
        if (request.IsDefaultShipping)
        {
            await _repo.ClearDefaultShippingAsync(profileId, exceptAddressId, ct);
        }

        if (request.IsDefaultBilling)
        {
            await _repo.ClearDefaultBillingAsync(profileId, exceptAddressId, ct);
        }
    }

    // Copies request fields onto an address entity (shared by create + update). Country
    // is upper-cased to a canonical ISO-3166 alpha-2 form.
    private static void ApplyAddressFields(Address address, AddressRequest request)
    {
        address.Line1 = request.Line1.Trim();
        address.Line2 = Trimmed(request.Line2);
        address.City = request.City.Trim();
        address.Region = Trimmed(request.Region);
        address.PostalCode = request.PostalCode.Trim();
        address.Country = request.Country.Trim().ToUpperInvariant();
        address.IsDefaultShipping = request.IsDefaultShipping;
        address.IsDefaultBilling = request.IsDefaultBilling;
    }

    // Trims, collapsing blank/whitespace-only input to null (the API treats null as "unset").
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
