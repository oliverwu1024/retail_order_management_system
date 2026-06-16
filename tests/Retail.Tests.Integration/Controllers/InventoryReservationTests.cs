using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Inventory reservation + expiry-sweep tests on real SQL Server (Story 2.3). These drive the
/// services directly through DI scopes — the HTTP reserve path arrives with checkout in the
/// next chunk — and seed their data through the DbContext for a focused setup.
/// </summary>
/// <remarks>
/// The headline test is the last-unit race: two carts reserve the same single unit
/// concurrently (each in its own scope = its own DbContext = its own connection), and the
/// RowVersion-guarded bump must let exactly one win. Optimistic concurrency only behaves
/// correctly on a real engine, hence SQL Server rather than SQLite.
/// </remarks>
[Collection("api")]
public class InventoryReservationTests
{
    private const int PriceCents = 1999;
    private readonly ApiFactory _factory;

    public InventoryReservationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ReserveCart_WhenStockAvailable_BumpsReserved()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedOpenCartAsync(variantId, quantity: 2, expiresAt: Future());

        await ReserveAsync(cartId);

        (int onHand, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(5, onHand); // OnHand is untouched until commit (payment)
        Assert.Equal(2, reserved);
        Assert.Equal(ReservationStatus.Active, await ReadCartReservationStatusAsync(cartId));
    }

    [Fact]
    public async Task ReserveCart_WhenInsufficientStock_ThrowsOutOfStock()
    {
        Guid variantId = await SeedVariantAsync(onHand: 1);
        Guid cartId = await SeedOpenCartAsync(variantId, quantity: 2, expiresAt: Future());

        await Assert.ThrowsAsync<OutOfStockException>(() => ReserveAsync(cartId));

        (_, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(0, reserved); // nothing held — the whole reserve rolled back
    }

    [Fact]
    public async Task TwoCartsRaceForLastUnit_ExactlyOneReservationWins()
    {
        Guid variantId = await SeedVariantAsync(onHand: 1);
        Guid cartA = await SeedOpenCartAsync(variantId, quantity: 1, expiresAt: Future());
        Guid cartB = await SeedOpenCartAsync(variantId, quantity: 1, expiresAt: Future());

        // Each reserve runs in its own scope/DbContext so the two genuinely contend at the DB.
        Exception?[] outcomes = await Task.WhenAll(TryReserveAsync(cartA), TryReserveAsync(cartB));

        Assert.Equal(1, outcomes.Count(o => o is null)); // exactly one winner
        Assert.Contains(outcomes, o => o is OutOfStockException or ConcurrencyException); // loser got a 409-mapped error
        (_, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(1, reserved); // only the one unit is held
    }

    [Fact]
    public async Task Sweep_AbandonsExpiredCart_AndReleasesItsReservation()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        // Born already expired — reserve still works (the reserve path filters on Open, not expiry).
        Guid cartId = await SeedOpenCartAsync(variantId, quantity: 2, expiresAt: Past());
        await ReserveAsync(cartId);

        int swept = await SweepAsync();

        Assert.True(swept >= 1);
        Assert.Equal(CartStatus.Abandoned, await ReadCartStatusAsync(cartId));
        Assert.Equal(ReservationStatus.Released, await ReadCartReservationStatusAsync(cartId));
        (_, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(0, reserved); // the held stock was handed back
    }

    // ── seeding (via DbContext) ─────────────────────────────────────────────────

    private async Task<Guid> SeedVariantAsync(int onHand)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category,
            Sku = $"SKU-{suffix}",
            Slug = $"product-{suffix}",
            Name = $"Product {suffix}",
            IsPublished = true,
        };
        var variant = new ProductVariant
        {
            Product = product,
            Sku = $"VAR-{suffix}",
            Options = new Dictionary<string, string> { ["size"] = "M" },
            PriceCents = PriceCents,
            IsActive = true,
        };
        var inventory = new InventoryItem { Variant = variant, OnHand = onHand };

        db.AddRange(category, product, variant, inventory);
        await db.SaveChangesAsync();
        return variant.Id;
    }

    private async Task<Guid> SeedOpenCartAsync(Guid variantId, int quantity, DateTimeOffset expiresAt)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var cart = new Cart
        {
            Status = CartStatus.Open,
            AnonymousKey = Guid.NewGuid().ToString(),
            ExpiresAt = expiresAt,
        };
        cart.Items.Add(new CartItem
        {
            ProductVariantId = variantId,
            Quantity = quantity,
            UnitPriceCentsSnapshot = PriceCents,
        });

        db.Carts.Add(cart);
        await db.SaveChangesAsync();
        return cart.Id;
    }

    // ── service drivers (each in its own scope) ─────────────────────────────────

    private async Task ReserveAsync(Guid cartId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IInventoryReservationService svc = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        await svc.ReserveCartAsync(cartId, CancellationToken.None);
    }

    private async Task<Exception?> TryReserveAsync(Guid cartId)
    {
        try
        {
            await ReserveAsync(cartId);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private async Task<int> SweepAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        ICartSweepService svc = scope.ServiceProvider.GetRequiredService<ICartSweepService>();
        return await svc.SweepExpiredCartsAsync(CancellationToken.None);
    }

    // ── readback ─────────────────────────────────────────────────────────────────

    private async Task<(int OnHand, int Reserved)> ReadStockAsync(Guid variantId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        InventoryItem item = await db.InventoryItems.AsNoTracking().FirstAsync(i => i.ProductVariantId == variantId);
        return (item.OnHand, item.Reserved);
    }

    private async Task<CartStatus> ReadCartStatusAsync(Guid cartId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return (await db.Carts.AsNoTracking().FirstAsync(c => c.Id == cartId)).Status;
    }

    private async Task<ReservationStatus> ReadCartReservationStatusAsync(Guid cartId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return (await db.InventoryReservations.AsNoTracking().FirstAsync(r => r.CartId == cartId)).Status;
    }

    private DateTimeOffset Future() => _factory.Services
        .GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(15);

    private DateTimeOffset Past() => _factory.Services
        .GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(-5);
}
