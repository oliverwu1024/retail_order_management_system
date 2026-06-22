using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Services;

/// <summary>
/// Demand-forecasting service (Phase 5B forecasting Chunk 2). Drives <see cref="IForecastService"/>
/// directly and asserts only on each test's own variant — the Testcontainers DB is shared, and
/// RefreshAsync scans every active variant (PHASE_5B_FORECAST_SCOPE §14).
/// </summary>
[Collection("api")]
public class ForecastServiceTests
{
    private readonly ApiFactory _factory;

    public ForecastServiceTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RefreshAsync_VariantWithSufficientHistory_WritesForecastAndReorderHint()
    {
        Guid variantId = await SeedVariantWithOrdersAsync(orderCount: 15, daySpacing: 3);

        await RefreshAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        DemandForecast? f = await db.DemandForecasts.AsNoTracking()
            .Where(x => x.ProductVariantId == variantId)
            .OrderByDescending(x => x.GeneratedAt).FirstOrDefaultAsync();
        Assert.NotNull(f);
        Assert.Equal((short)14, f!.Horizon);
        Assert.True(f.ForecastedQty >= 0);
        Assert.True(f.LowerBound <= f.ForecastedQty && f.ForecastedQty <= f.UpperBound);
        Assert.True(f.Confidence > 0);

        ReorderHint? hint = await db.ReorderHints.AsNoTracking().FirstOrDefaultAsync(h => h.ProductVariantId == variantId);
        Assert.NotNull(hint);
        Assert.True(hint!.RecommendedOrderQty >= 0);
        Assert.False(string.IsNullOrWhiteSpace(hint.Reasoning));
    }

    [Fact]
    public async Task RefreshAsync_ColdStartVariant_Skipped()
    {
        // History spans only ~10 days (< MinHistoryDays 30) → no row.
        Guid variantId = await SeedVariantWithOrdersAsync(orderCount: 5, daySpacing: 2);

        await RefreshAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        Assert.False(await db.DemandForecasts.AnyAsync(x => x.ProductVariantId == variantId));
        Assert.False(await db.ReorderHints.AnyAsync(h => h.ProductVariantId == variantId));
    }

    [Fact]
    public async Task RefreshAsync_SecondRun_UpsertsOneHint_AppendsForecast()
    {
        Guid variantId = await SeedVariantWithOrdersAsync(orderCount: 15, daySpacing: 3);

        await RefreshAsync();
        await RefreshAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        Assert.Equal(1, await db.ReorderHints.CountAsync(h => h.ProductVariantId == variantId));
        Assert.Equal(2, await db.DemandForecasts.CountAsync(f => f.ProductVariantId == variantId));
    }

    [Fact]
    public async Task DismissReorderHint_HidesIt()
    {
        Guid variantId = await SeedVariantWithOrdersAsync(orderCount: 15, daySpacing: 3);
        await RefreshAsync();

        Guid hintId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
            hintId = await db.ReorderHints.Where(h => h.ProductVariantId == variantId).Select(h => h.Id).SingleAsync();
            await scope.ServiceProvider.GetRequiredService<IForecastService>().DismissReorderHintAsync(hintId);
        }

        using IServiceScope read = _factory.Services.CreateScope();
        RetailDbContext db2 = read.ServiceProvider.GetRequiredService<RetailDbContext>();
        Assert.True(await db2.ReorderHints.Where(h => h.Id == hintId).Select(h => h.Dismissed).SingleAsync());
    }

    [Fact]
    public async Task DismissReorderHint_Unknown_Throws()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IForecastService service = scope.ServiceProvider.GetRequiredService<IForecastService>();
        await Assert.ThrowsAsync<NotFoundException>(() => service.DismissReorderHintAsync(Guid.NewGuid()));
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task RefreshAsync()
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IForecastService>().RefreshAsync();
    }

    // Seeds one active variant + `orderCount` paid orders, one per distinct day spaced `daySpacing`
    // days apart (so span ≈ orderCount × daySpacing, non-zero-days = orderCount).
    private async Task<Guid> SeedVariantWithOrdersAsync(int orderCount, int daySpacing, int qty = 4, int onHand = 5)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        DateTimeOffset now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        const int price = 2_000;

        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category,
            Sku = $"SKU-{suffix}",
            Slug = $"p-{suffix}",
            Name = $"Product {suffix}",
            IsPublished = true,
        };
        var variant = new ProductVariant
        {
            Product = product,
            Sku = $"VAR-{suffix}",
            Options = new Dictionary<string, string> { ["size"] = "M" },
            PriceCents = price,
            IsActive = true,
        };
        var inventory = new InventoryItem { Variant = variant, OnHand = onHand };
        db.AddRange(category, product, variant, inventory);

        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        for (int i = 0; i < orderCount; i++)
        {
            var order = new Order
            {
                GuestEmail = $"g-{suffix}-{i}@test.local",
                Status = OrderStatus.Paid,
                SubtotalCents = price * qty,
                TaxCents = 0,
                ShippingCents = 0,
                TotalCents = price * qty,
                ShippingAddress = address,
                BillingAddress = address,
                PlacedAt = now.AddDays(-i * daySpacing),
            };
            order.Lines.Add(new OrderLine
            {
                ProductVariant = variant,
                Quantity = qty,
                UnitPriceCents = price,
                LineTotalCents = price * qty,
                SkuSnapshot = variant.Sku,
                NameSnapshot = product.Name,
            });
            db.Orders.Add(order);
        }

        await db.SaveChangesAsync();
        return variant.Id;
    }
}
