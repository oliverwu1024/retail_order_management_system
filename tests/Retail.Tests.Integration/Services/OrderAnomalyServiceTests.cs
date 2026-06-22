using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Services;

/// <summary>
/// Order-anomaly engine (Phase 5B Chunk 2). Drives <see cref="IOrderAnomalyService.EvaluateOrderAsync"/>
/// per single order so each test asserts only on its own data — the Testcontainers DB is shared across
/// the class run (PHASE_5B_SCOPE §14).
/// </summary>
[Collection("api")]
public class OrderAnomalyServiceTests
{
    // A tight, varied per-customer baseline (~$50, σ > 0) so rule 1 keys off the customer (deterministic),
    // not the shared global pool.
    private static readonly int[] NormalHistory = { 5000, 5200, 4800, 5100, 4900, 5300, 4700, 5050 };

    private readonly ApiFactory _factory;

    public OrderAnomalyServiceTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task EvaluateOrder_TotalFarAboveCustomerBaseline_FlagsZScore()
    {
        Guid orderId = await SeedScenarioAsync(NormalHistory, testTotalCents: 80_000, country: "AU", qty: 1);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.NotNull(anomaly);
        Assert.True(anomaly!.Score > 3, $"expected a Z-score > 3 but was {anomaly.Score}");
        Assert.Contains("far above", anomaly.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateOrder_BigTotalAgainstWideBaseline_FlagsZScore()
    {
        // Mirrors the demo seeder's shape: a buyer whose normal orders span a WIDE range ($60–$480),
        // then the injected huge order (~$3,960 = every variant × qty 5). The log transform must still
        // place it past |Z| > 3 — the property the first dev-seed run missed before the seeder was
        // tightened + the anomaly enlarged.
        int[] wideHistory = { 6_000, 9_000, 13_000, 18_000, 24_000, 30_000, 38_000, 48_000 };
        Guid orderId = await SeedScenarioAsync(wideHistory, testTotalCents: 396_000, country: "AU", qty: 1);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.NotNull(anomaly);
        Assert.True(anomaly!.Score > 3, $"expected a Z-score > 3 but was {anomaly.Score}");
        Assert.Contains("far above", anomaly.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateOrder_NormalOrder_NotFlagged()
    {
        Guid orderId = await SeedScenarioAsync(NormalHistory, testTotalCents: 5_100, country: "AU", qty: 1);

        Assert.Null(await EvaluateAndGetAsync(orderId));
    }

    [Fact]
    public async Task EvaluateOrder_NewShippingCountry_Flags()
    {
        // Normal total + qty, but a country the buyer (all-AU history) has never used.
        Guid orderId = await SeedScenarioAsync(NormalHistory, testTotalCents: 5_100, country: "US", qty: 1);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.NotNull(anomaly);
        Assert.Contains("new country", anomaly!.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("US", anomaly.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateOrder_QuantitySpike_Flags()
    {
        Guid orderId = await SeedScenarioAsync(NormalHistory, testTotalCents: 5_100, country: "AU", qty: 6);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.NotNull(anomaly);
        Assert.Contains("quantity", anomaly!.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateOrder_FirstEverOrder_NotNewCountryFlagged()
    {
        // No prior orders → rule 2 must not fire (a first order has no "prior countries").
        Guid orderId = await SeedScenarioAsync(Array.Empty<int>(), testTotalCents: 5_100, country: "US", qty: 1);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.True(anomaly is null || !anomaly.Reason.Contains("new country", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task EvaluateOrder_GuestQuantitySpike_FlagsQuantityOnly()
    {
        // Guest (no profile/history): rule 3 fires; rule 2 can't (no prior orders).
        Guid orderId = await SeedScenarioAsync(Array.Empty<int>(), testTotalCents: 5_100, country: "US", qty: 7, guest: true);

        OrderAnomaly? anomaly = await EvaluateAndGetAsync(orderId);

        Assert.NotNull(anomaly);
        Assert.Contains("quantity", anomaly!.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("new country", anomaly.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvaluateOrder_SecondCall_IsIdempotent()
    {
        Guid orderId = await SeedScenarioAsync(NormalHistory, testTotalCents: 80_000, country: "AU", qty: 1);

        await EvaluateAndGetAsync(orderId); // flags
        await EvaluateAndGetAsync(orderId); // must no-op (already flagged)

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        int rows = await db.OrderAnomalies.CountAsync(a => a.OrderId == orderId);
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task ScanAsync_FlagsRecentAnomalousOrders_NotNormalOnes()
    {
        // Exercises the batch scan path (candidate query + per-buyer/global baseline preload + AddRange),
        // scoped to this test's own orders since the Testcontainers DB is shared.
        Guid anomalous = await SeedScenarioAsync(NormalHistory, testTotalCents: 5_100, country: "AU", qty: 9);
        Guid normal = await SeedScenarioAsync(NormalHistory, testTotalCents: 5_100, country: "AU", qty: 1);

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOrderAnomalyService>().ScanAsync();
        }

        using IServiceScope read = _factory.Services.CreateScope();
        RetailDbContext db = read.ServiceProvider.GetRequiredService<RetailDbContext>();
        Assert.True(await db.OrderAnomalies.AnyAsync(a => a.OrderId == anomalous), "the qty-9 order should be flagged");
        Assert.False(await db.OrderAnomalies.AnyAsync(a => a.OrderId == normal), "the normal order should not be flagged");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    // Seeds a unique variant + (for a member) a buyer with the given history totals + the order under test.
    private async Task<Guid> SeedScenarioAsync(int[] priorTotals, int testTotalCents, string country, int qty, bool guest = false)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        DateTimeOffset now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();

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
            PriceCents = 5_000,
            IsActive = true,
        };
        db.AddRange(category, product, variant);

        Guid? profileId = null;
        if (!guest)
        {
            string email = $"anom-{suffix}@test.local";
            var user = new ApplicationUser
            {
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                DisplayName = "Anom Buyer",
                EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = "Anom Buyer" };
            db.Users.Add(user);
            db.CustomerProfiles.Add(profile);
            profileId = profile.Id;

            for (int i = 0; i < priorTotals.Length; i++)
            {
                db.Orders.Add(BuildOrder(variant, profileId, null, priorTotals[i], "AU", 1, now.AddDays(-30 + i), suffix, $"h{i}"));
            }
        }

        string? guestEmail = guest ? $"guest-{suffix}@test.local" : null;
        Order testOrder = BuildOrder(variant, profileId, guestEmail, testTotalCents, country, qty, now.AddHours(-1), suffix, "t");
        db.Orders.Add(testOrder);

        await db.SaveChangesAsync();
        return testOrder.Id;
    }

    private static Order BuildOrder(
        ProductVariant variant, Guid? profileId, string? guestEmail,
        int totalCents, string country, int qty, DateTimeOffset placedAt, string suffix, string tag)
    {
        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "X", PostalCode = "0000", Country = country };
        var order = new Order
        {
            CustomerProfileId = profileId,
            GuestEmail = guestEmail,
            Status = OrderStatus.Paid,
            SubtotalCents = totalCents,
            TaxCents = 0,
            ShippingCents = 0,
            TotalCents = totalCents,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = placedAt,
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariant = variant,
            Quantity = qty,
            UnitPriceCents = variant.PriceCents,
            LineTotalCents = variant.PriceCents * qty,
            SkuSnapshot = variant.Sku,
            NameSnapshot = "Product",
        });
        order.Payments.Add(new Payment
        {
            Provider = "stripe",
            StripePaymentIntentId = $"pi_{suffix}_{tag}",
            AmountCents = totalCents,
            Currency = "AUD",
            Status = PaymentStatus.Succeeded,
        });
        return order;
    }

    private async Task<OrderAnomaly?> EvaluateAndGetAsync(Guid orderId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        IOrderAnomalyService service = scope.ServiceProvider.GetRequiredService<IOrderAnomalyService>();
        await service.EvaluateOrderAsync(orderId);

        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.OrderAnomalies.AsNoTracking().FirstOrDefaultAsync(a => a.OrderId == orderId);
    }
}
