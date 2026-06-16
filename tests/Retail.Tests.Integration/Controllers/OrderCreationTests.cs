using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Order-creation tests on real SQL Server (Story 2.2, 3b-ii). Drives
/// <see cref="IOrderCreationService"/> directly (the webhook that calls it arrives in 3c),
/// seeding through the DbContext for a focused setup. Verifies the paid order is assembled,
/// stock is committed (OnHand drops, holds clear), the cart converts, and redelivery is a no-op.
/// </summary>
[Collection("api")]
public class OrderCreationTests
{
    private const int PriceCents = 1999;
    private readonly ApiFactory _factory;

    public OrderCreationTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CompleteCheckout_CreatesPaidOrder_CommitsInventory_ConvertsCart()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedOpenCartAsync(variantId, quantity: 2);
        await ReserveAsync(cartId);

        Order order = await CompleteAsync(NewCompletion(cartId, customerProfileId: null, email: "guest@test.local"));

        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.True(order.OrderNumber >= 10001); // assigned by Seq_OrderNumber
        Assert.Null(order.CustomerProfileId);
        Assert.Equal("guest@test.local", order.GuestEmail);
        // subtotal 1999×2 = 3998; GST 10% = 400 (399.8 rounded up); total 4398.
        Assert.Equal(3998, order.SubtotalCents);
        Assert.Equal(400, order.TaxCents);
        Assert.Equal(4398, order.TotalCents);

        (int onHand, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(3, onHand); // 5 − 2 committed
        Assert.Equal(0, reserved); // the hold cleared

        Assert.Equal(CartStatus.Converted, await ReadCartStatusAsync(cartId));
    }

    [Fact]
    public async Task CompleteCheckout_IsIdempotent_OnRedelivery()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedOpenCartAsync(variantId, quantity: 2);
        await ReserveAsync(cartId);

        CheckoutCompletion completion = NewCompletion(cartId, customerProfileId: null, email: "guest@test.local");
        Order first = await CompleteAsync(completion);
        Order second = await CompleteAsync(completion); // same session redelivered

        Assert.Equal(first.Id, second.Id); // same order, not a duplicate
        (int onHand, _) = await ReadStockAsync(variantId);
        Assert.Equal(3, onHand); // inventory was NOT decremented twice
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static CheckoutCompletion NewCompletion(Guid cartId, Guid? customerProfileId, string email) =>
        new(
            StripeSessionId: $"cs_test_{Guid.NewGuid():N}",
            PaymentIntentId: $"pi_test_{Guid.NewGuid():N}",
            CartId: cartId,
            CustomerProfileId: customerProfileId,
            CustomerEmail: email,
            ShippingAddress: Address(),
            BillingAddress: Address());

    private static OrderAddressSnapshot Address() => new()
    {
        RecipientName = "Test Buyer",
        Line1 = "1 Test St",
        City = "Sydney",
        Region = "NSW",
        PostalCode = "2000",
        Country = "AU",
    };

    private async Task<Order> CompleteAsync(CheckoutCompletion completion)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IOrderCreationService svc = scope.ServiceProvider.GetRequiredService<IOrderCreationService>();
        return await svc.CreateOrderFromCheckoutAsync(completion, CancellationToken.None);
    }

    private async Task ReserveAsync(Guid cartId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IInventoryReservationService svc = scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
        await svc.ReserveCartAsync(cartId, CancellationToken.None);
    }

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

    private async Task<Guid> SeedOpenCartAsync(Guid variantId, int quantity)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var cart = new Cart
        {
            Status = CartStatus.Open,
            AnonymousKey = Guid.NewGuid().ToString(),
            ExpiresAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow().AddMinutes(30),
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
}
