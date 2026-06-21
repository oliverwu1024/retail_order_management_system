using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Seeding;

/// <summary>
/// DEVELOPMENT-ONLY demo data: seeds ~6 months of synthetic <see cref="Order"/> history so the
/// order-anomaly scan (Phase-5B Chunk 2), the Risk Queue, and later demand forecasting have real
/// data on a fresh dev run (PHASE_5B_SCOPE §3.7). The series carries a weekly cycle + a mild upward
/// trend + noise, and a few orders are deliberately anomalous (a huge total, a never-seen shipping
/// country, a quantity spike) so the scan has something to flag.
/// </summary>
/// <remarks>
/// <para>
/// DETERMINISTIC: all counts/choices come from a fixed-seed <see cref="Random"/>, so a given dev DB
/// always gets the same shape (reproducible demo + tests).
/// </para>
/// <para>
/// DIRECT INSERT, INVARIANTS HONORED: orders are inserted straight onto the context (bypassing
/// <c>OrderCreationService</c>), so the seeder satisfies that path's invariants itself — the
/// member-XOR-guest <c>CK_Order_Identity</c> CHECK (every demo order is a member), a unique
/// <c>Payment.StripeSessionId</c> per order, the <c>ShippingAddressJson</c> snapshot (incl.
/// <c>Country</c> for rule 2), and consistent subtotal/tax/total. <c>OrderNumber</c> is left unset —
/// the <c>Seq_OrderNumber</c> sequence assigns it on insert. No stock is moved (these are historical
/// rows, not a real checkout).
/// </para>
/// <para>
/// IDEMPOTENT + DEV-ONLY: no-op outside Development; the run is gated on a sentinel demo customer.
/// Customers + orders commit in ONE <c>SaveChanges</c>, so the sentinel is atomic — a partial seed
/// can't leave the guard "satisfied". Skips gracefully if there are no active variants to order.
/// </para>
/// </remarks>
public sealed class OrderDemoSeeder
{
    private const int Days = 180;          // ~6 months of history
    private const int CustomerCount = 6;   // each ends up with plenty of orders for a Z-score baseline
    private const double BaseDaily = 1.5;  // average orders/day before seasonality + noise
    private const int RandomSeed = 5_2025; // fixed → reproducible
    private const string SentinelEmail = "demo-buyer-0@demo.local";

    private readonly RetailDbContext _db;
    private readonly IHostEnvironment _env;
    private readonly TimeProvider _clock;
    private readonly ILogger<OrderDemoSeeder> _logger;

    public OrderDemoSeeder(
        RetailDbContext db,
        IHostEnvironment env,
        TimeProvider clock,
        ILogger<OrderDemoSeeder> logger)
    {
        _db = db;
        _env = env;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_env.IsDevelopment() || await _db.Users.AnyAsync(u => u.Email == SentinelEmail, ct))
        {
            return; // dev-only; idempotent (sentinel is committed atomically with the orders)
        }

        List<ProductVariant> variants = await _db.ProductVariants.AsNoTracking()
            .Include(v => v.Product)
            .Where(v => v.IsActive)
            .OrderByDescending(v => v.PriceCents) // priciest first → easy to pick for the big-total anomaly
            .ThenBy(v => v.Sku)
            .ToListAsync(ct);
        if (variants.Count == 0)
        {
            _logger.LogInformation("Order demo seed skipped: no active variants to order.");
            return;
        }

        var rng = new Random(RandomSeed);
        DateTimeOffset now = _clock.GetUtcNow();
        DateTimeOffset start = now.AddDays(-Days);

        // ── Demo buyers (members so the per-customer rules have a baseline) ───
        var profiles = new List<CustomerProfile>(CustomerCount);
        for (int i = 0; i < CustomerCount; i++)
        {
            (ApplicationUser user, CustomerProfile profile) = BuildDemoBuyer(i);
            _db.Users.Add(user);
            _db.CustomerProfiles.Add(profile); // profile.Id assigned now (client-generated GUID key)
            profiles.Add(profile);
        }

        int seq = 0; // drives unique Payment.StripeSessionId values

        // ── 180-day baseline: weekly cycle + mild trend + noise, all in AUD/AU ──
        for (int d = 0; d < Days; d++)
        {
            DateTimeOffset day = start.AddDays(d);
            double weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 1.6 : 1.0;
            double trend = 1.0 + 0.5 * d / Days;                 // +50% across the window
            double noise = 0.6 + rng.NextDouble() * 0.8;          // 0.6–1.4
            int count = (int)Math.Round(BaseDaily * weekend * trend * noise);

            for (int k = 0; k < count; k++)
            {
                CustomerProfile buyer = profiles[rng.Next(profiles.Count)];
                List<(ProductVariant Variant, int Qty)> lines = PickNormalLines(variants, rng);
                DateTimeOffset placedAt = day.AddHours(rng.Next(8, 21)).AddMinutes(rng.Next(60));
                _db.Orders.Add(BuildOrder(buyer.Id, placedAt, lines, "AU", seq++));
            }
        }

        // ── Injected anomalies (recent, so the 14-day scan window catches them) ──
        DateTimeOffset recent = now.AddDays(-1);

        // (1) Z-score: a huge total — 5 distinct variants × qty 5 (no single line > 5, so it reads as
        // a spend anomaly, not a quantity one).
        List<(ProductVariant, int)> bigLines = variants.Take(Math.Min(5, variants.Count))
            .Select(v => (v, 5)).ToList();
        _db.Orders.Add(BuildOrder(profiles[0].Id, recent, bigLines, "AU", seq++));

        // (2) New shipping country: an otherwise-normal order shipping to a country this buyer (all-AU
        // history) has never used.
        _db.Orders.Add(BuildOrder(profiles[1].Id, recent, new[] { (variants[^1], 2) }, "US", seq++));

        // (3) Quantity spike: a single line of 9 units (> 5).
        _db.Orders.Add(BuildOrder(profiles[2].Id, recent, new[] { (variants[^1], 9) }, "AU", seq++));

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Order demo seed: created {Count} synthetic orders across {Customers} buyers over {Days} days, " +
            "incl. 3 injected anomalies (Development only).",
            seq, CustomerCount, Days);
    }

    // 1–3 lines of distinct variants, each qty 1–3 → normal, never trips a rule.
    private static List<(ProductVariant Variant, int Qty)> PickNormalLines(List<ProductVariant> variants, Random rng)
    {
        int lineCount = Math.Min(1 + rng.Next(3), variants.Count);
        var chosen = new List<(ProductVariant, int)>(lineCount);
        var used = new HashSet<int>();
        while (chosen.Count < lineCount)
        {
            int idx = rng.Next(variants.Count);
            if (used.Add(idx))
            {
                chosen.Add((variants[idx], 1 + rng.Next(3)));
            }
        }

        return chosen;
    }

    // Assembles a full order graph (order + lines + breakdown + payment), mirroring OrderCreationService.
    private Order BuildOrder(
        Guid customerProfileId,
        DateTimeOffset placedAt,
        IReadOnlyList<(ProductVariant Variant, int Qty)> lines,
        string country,
        int seq)
    {
        int subtotal = lines.Sum(l => l.Variant.PriceCents * l.Qty);
        int tax = (int)Math.Round(subtotal * 0.10, MidpointRounding.AwayFromZero); // flat 10% GST (MVP)
        int total = subtotal + tax;                                                // free shipping (MVP)

        var address = new OrderAddressSnapshot
        {
            RecipientName = "Demo Buyer",
            Line1 = "1 Test Street",
            City = country == "AU" ? "Sydney" : "Seattle",
            Region = country == "AU" ? "NSW" : "WA",
            PostalCode = country == "AU" ? "2000" : "98101",
            Country = country,
        };

        var order = new Order
        {
            CustomerProfileId = customerProfileId,
            Status = OrderStatus.Paid,
            SubtotalCents = subtotal,
            TaxCents = tax,
            ShippingCents = 0,
            TotalCents = total,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = placedAt,
            // OrderNumber assigned by Seq_OrderNumber on insert; RowVersion DB-generated.
        };

        foreach ((ProductVariant variant, int qty) in lines)
        {
            order.Lines.Add(new OrderLine
            {
                ProductVariantId = variant.Id,
                Quantity = qty,
                UnitPriceCents = variant.PriceCents,
                LineTotalCents = variant.PriceCents * qty,
                SkuSnapshot = variant.Sku,
                NameSnapshot = variant.Product?.Name ?? string.Empty,
            });
        }

        order.PriceBreakdown = new OrderPriceBreakdown
        {
            SubtotalCents = subtotal,
            ShippingCents = 0,
            TaxCents = tax,
            TotalCents = total,
        };

        order.Payments.Add(new Payment
        {
            Provider = "stripe",
            StripeSessionId = $"cs_seed_{seq}",
            StripePaymentIntentId = $"pi_seed_{seq}",
            AmountCents = total,
            Currency = "AUD",
            Status = PaymentStatus.Succeeded,
        });

        return order;
    }

    private static (ApplicationUser User, CustomerProfile Profile) BuildDemoBuyer(int index)
    {
        string email = $"demo-buyer-{index}@demo.local";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = $"Demo Buyer {index + 1}",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = user.DisplayName! };
        return (user, profile);
    }
}
