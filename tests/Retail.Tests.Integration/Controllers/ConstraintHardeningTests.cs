using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// DB-level invariant tests for the 0007 constraint-hardening migration (review items B2/B3):
/// at most one Open cart per owner (filtered UNIQUE indexes) and the InventoryReservation
/// CartId-XOR-OrderId CHECK. These assert the database itself rejects states the application would
/// never write — the safety net beneath the service-layer guards.
/// </summary>
[Collection("api")]
public class ConstraintHardeningTests
{
    private readonly ApiFactory _factory;

    public ConstraintHardeningTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Cart_TwoOpenCartsSameAnonymousKey_Rejected()
    {
        string key = Guid.NewGuid().ToString();
        await InsertGuestCartAsync(key, CartStatus.Open); // first is fine

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => InsertGuestCartAsync(key, CartStatus.Open));
    }

    [Fact]
    public async Task Cart_OpenAndAbandonedSameAnonymousKey_Allowed()
    {
        // The unique index is filtered to Status = Open, so a tombstoned (Abandoned) cart never
        // collides with a fresh Open one for the same key (e.g. after a sweep + re-add).
        string key = Guid.NewGuid().ToString();
        await InsertGuestCartAsync(key, CartStatus.Abandoned);
        await InsertGuestCartAsync(key, CartStatus.Open); // must not throw
    }

    [Fact]
    public async Task Cart_TwoOpenCartsBothNullAnonymousKey_Allowed()
    {
        // Member carts carry a null AnonymousKey; the filter excludes NULL keys, so two such rows
        // must NOT be treated as colliding.
        await InsertCartAsync(anonymousKey: null, customerProfileId: null, status: CartStatus.Open);
        await InsertCartAsync(anonymousKey: null, customerProfileId: null, status: CartStatus.Open); // must not throw
    }

    [Fact]
    public async Task Cart_TwoOpenCartsSameProfile_Rejected()
    {
        Guid profileId = await RegisterCustomerProfileAsync();
        await InsertCartAsync(anonymousKey: null, customerProfileId: profileId, status: CartStatus.Open);

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => InsertCartAsync(anonymousKey: null, customerProfileId: profileId, status: CartStatus.Open));
    }

    [Fact]
    public async Task Reservation_WithNeitherCartNorOrder_Rejected()
    {
        // The CHECK requires exactly one owner; a row with both null violates it (the both-set case
        // is the symmetric half of the same predicate).
        Guid inventoryItemId = await SeedInventoryItemAsync();

        await Assert.ThrowsAnyAsync<DbUpdateException>(
            () => InsertReservationAsync(inventoryItemId, cartId: null, orderId: null));
    }

    // ── helpers ─────────────────────────────────────────────────────────────────

    private Task InsertGuestCartAsync(string anonymousKey, CartStatus status) =>
        InsertCartAsync(anonymousKey, customerProfileId: null, status);

    private async Task InsertCartAsync(string? anonymousKey, Guid? customerProfileId, CartStatus status)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        db.Carts.Add(new Cart
        {
            Status = status,
            AnonymousKey = anonymousKey,
            CustomerProfileId = customerProfileId,
            ExpiresAt = Now().AddMinutes(30),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedInventoryItemAsync()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category, Sku = $"SKU-{suffix}", Slug = $"product-{suffix}", Name = $"Product {suffix}", IsPublished = true,
        };
        var variant = new ProductVariant
        {
            Product = product, Sku = $"VAR-{suffix}", Options = new Dictionary<string, string> { ["size"] = "M" }, PriceCents = 1999, IsActive = true,
        };
        var inventory = new InventoryItem { Variant = variant, OnHand = 5 };

        db.AddRange(category, product, variant, inventory);
        await db.SaveChangesAsync();
        return inventory.Id;
    }

    private async Task InsertReservationAsync(Guid inventoryItemId, Guid? cartId, Guid? orderId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        db.InventoryReservations.Add(new InventoryReservation
        {
            InventoryItemId = inventoryItemId,
            CartId = cartId,
            OrderId = orderId,
            Quantity = 1,
            Status = ReservationStatus.Active,
            ExpiresAt = Now().AddMinutes(15),
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> RegisterCustomerProfileAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        {
            Content = JsonContent.Create(
                new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        (await client.SendAsync(request)).EnsureSuccessStatusCode();

        // The profile is lazily created; GET it to learn its id (registration does not create a cart).
        JsonElement profile = (await (await client.GetAsync("/api/v1/profile"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        return Guid.Parse(profile.GetProperty("id").GetString()!);
    }

    private DateTimeOffset Now() => _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow();

    private static string ExtractCookie(HttpResponseMessage response, string cookieName)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies),
            $"Expected a Set-Cookie header carrying '{cookieName}'.");
        string? setCookie = cookies!.FirstOrDefault(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal));
        Assert.NotNull(setCookie);
        string afterName = setCookie!.Substring(cookieName.Length + 1);
        int semicolon = afterName.IndexOf(';');
        return semicolon >= 0 ? afterName.Substring(0, semicolon) : afterName;
    }
}
