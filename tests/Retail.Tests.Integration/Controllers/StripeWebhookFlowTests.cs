using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Services;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Stripe webhook tests on real SQL Server (Story 2.2, 3c). Hermetic — events are SELF-SIGNED
/// with the test webhook secret and POSTed to the real endpoint, so the signature check runs for
/// real with no network. Verifies a completed checkout becomes a paid order, that redelivery is
/// idempotent, and that a bad signature is rejected.
/// </summary>
[Collection("api")]
public class StripeWebhookFlowTests
{
    private const int PriceCents = 1999;
    private readonly ApiFactory _factory;

    public StripeWebhookFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Webhook_CheckoutSessionCompleted_CreatesPaidOrder_AndCommitsStock()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedReservedCartAsync(variantId, quantity: 2);
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        string payload = BuildCompletedEvent($"evt_{Guid.NewGuid():N}", sessionId, cartId);

        HttpResponseMessage resp = await PostWebhookAsync(payload, Sign(payload, ApiFactory.TestWebhookSecret));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Order? order = await ReadOrderBySessionAsync(sessionId);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Paid, order!.Status);
        Assert.Equal("guest@test.local", order.GuestEmail);
        (int onHand, int reserved) = await ReadStockAsync(variantId);
        Assert.Equal(3, onHand); // 5 − 2 committed
        Assert.Equal(0, reserved);
        Assert.Equal(CartStatus.Converted, await ReadCartStatusAsync(cartId));
    }

    [Fact]
    public async Task Webhook_DuplicateDelivery_IsIdempotent()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedReservedCartAsync(variantId, quantity: 2);
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        // SAME event id both times → the dedup ledger short-circuits the second delivery.
        string payload = BuildCompletedEvent($"evt_{Guid.NewGuid():N}", sessionId, cartId);
        string signature = Sign(payload, ApiFactory.TestWebhookSecret);

        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(payload, signature)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(payload, signature)).StatusCode);

        Assert.Equal(1, await CountOrdersBySessionAsync(sessionId)); // exactly one order
        (int onHand, _) = await ReadStockAsync(variantId);
        Assert.Equal(3, onHand); // stock committed once, not twice
    }

    [Fact]
    public async Task Webhook_BadSignature_Returns400_AndCreatesNoOrder()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedReservedCartAsync(variantId, quantity: 2);
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        string payload = BuildCompletedEvent($"evt_{Guid.NewGuid():N}", sessionId, cartId);

        HttpResponseMessage resp = await PostWebhookAsync(payload, "t=123,v1=deadbeef"); // not our HMAC

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Null(await ReadOrderBySessionAsync(sessionId));
    }

    [Fact]
    public async Task Webhook_ChargeRefunded_RefundsOrder_AndRestocks()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedReservedCartAsync(variantId, quantity: 2);
        string sessionId = $"cs_test_{Guid.NewGuid():N}";

        // 1) Complete checkout → a Paid order (OnHand 5 → 3). The completed event sets the
        //    PaymentIntent to pi_test_{cartId:N} (see BuildCompletedEvent).
        string completed = BuildCompletedEvent($"evt_{Guid.NewGuid():N}", sessionId, cartId);
        Assert.Equal(HttpStatusCode.OK,
            (await PostWebhookAsync(completed, Sign(completed, ApiFactory.TestWebhookSecret))).StatusCode);
        Assert.Equal(3, (await ReadStockAsync(variantId)).OnHand);

        // 2) Refund that charge → Order Refunded + stock restored.
        string refunded = BuildRefundedEvent($"evt_{Guid.NewGuid():N}", $"pi_test_{cartId:N}");
        Assert.Equal(HttpStatusCode.OK,
            (await PostWebhookAsync(refunded, Sign(refunded, ApiFactory.TestWebhookSecret))).StatusCode);

        Order? order = await ReadOrderBySessionAsync(sessionId);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Refunded, order!.Status);
        Assert.Equal(5, (await ReadStockAsync(variantId)).OnHand); // restocked
    }

    [Fact]
    public async Task Webhook_ChargeRefunded_Partial_IsIgnored()
    {
        Guid variantId = await SeedVariantAsync(onHand: 5);
        Guid cartId = await SeedReservedCartAsync(variantId, quantity: 2);
        string sessionId = $"cs_test_{Guid.NewGuid():N}";

        // 1) Complete checkout → a Paid order (OnHand 5 → 3).
        string completed = BuildCompletedEvent($"evt_{Guid.NewGuid():N}", sessionId, cartId);
        Assert.Equal(HttpStatusCode.OK,
            (await PostWebhookAsync(completed, Sign(completed, ApiFactory.TestWebhookSecret))).StatusCode);
        Assert.Equal(3, (await ReadStockAsync(variantId)).OnHand);

        // 2) A PARTIAL refund (refunded:false) is acked (200) but must NOT flip the order to
        //    Refunded or restock — partial refunds are out of Phase-2 scope. Treating it as full
        //    would over-restock and corrupt the ledger.
        string partial = BuildPartialRefundedEvent($"evt_{Guid.NewGuid():N}", $"pi_test_{cartId:N}");
        Assert.Equal(HttpStatusCode.OK,
            (await PostWebhookAsync(partial, Sign(partial, ApiFactory.TestWebhookSecret))).StatusCode);

        Order? order = await ReadOrderBySessionAsync(sessionId);
        Assert.NotNull(order);
        Assert.Equal(OrderStatus.Paid, order!.Status);            // still Paid, not Refunded
        Assert.Equal(3, (await ReadStockAsync(variantId)).OnHand); // NOT restocked
    }

    // ── webhook plumbing ────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string signature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payments/stripe/webhook")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", signature);
        return _factory.CreateClient().SendAsync(request);
    }

    // Stripe's signing scheme: HMAC-SHA256 over "{timestamp}.{payload}", header "t=..,v1=..".
    private static string Sign(string payload, string secret)
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return $"t={timestamp},v1={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static string BuildCompletedEvent(string eventId, string sessionId, Guid cartId) => $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "checkout.session.completed",
          "data": {
            "object": {
              "id": "{{sessionId}}",
              "object": "checkout.session",
              "payment_intent": "pi_test_{{cartId:N}}",
              "metadata": { "cartId": "{{cartId}}" },
              "customer_details": {
                "email": "guest@test.local",
                "name": "Test Buyer",
                "address": { "line1": "1 Test St", "city": "Sydney", "state": "NSW", "postal_code": "2000", "country": "AU" }
              },
              "amount_total": 4398,
              "currency": "aud"
            }
          }
        }
        """;

    private static string BuildRefundedEvent(string eventId, string paymentIntentId) => $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "charge.refunded",
          "data": {
            "object": {
              "id": "ch_test_{{eventId}}",
              "object": "charge",
              "payment_intent": "{{paymentIntentId}}",
              "refunded": true,
              "amount": 4398,
              "amount_refunded": 4398
            }
          }
        }
        """;

    // A partial refund: charge.refunded fires, but Refunded == false and amount_refunded < amount.
    private static string BuildPartialRefundedEvent(string eventId, string paymentIntentId) => $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "type": "charge.refunded",
          "data": {
            "object": {
              "id": "ch_test_{{eventId}}",
              "object": "charge",
              "payment_intent": "{{paymentIntentId}}",
              "refunded": false,
              "amount": 4398,
              "amount_refunded": 500
            }
          }
        }
        """;

    // ── seeding (DbContext) + readback ──────────────────────────────────────────

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

    // Seeds an open cart with one line AND reserves it, so the webhook has holds to commit.
    private async Task<Guid> SeedReservedCartAsync(Guid variantId, int quantity)
    {
        Guid cartId;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
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
            cartId = cart.Id;
        }

        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IInventoryReservationService reservations =
                scope.ServiceProvider.GetRequiredService<IInventoryReservationService>();
            await reservations.ReserveCartAsync(cartId, CancellationToken.None);
        }

        return cartId;
    }

    private async Task<Order?> ReadOrderBySessionAsync(string sessionId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.Payments.AsNoTracking()
            .Where(p => p.StripeSessionId == sessionId)
            .Select(p => p.Order!)
            .FirstOrDefaultAsync();
    }

    private async Task<int> CountOrdersBySessionAsync(string sessionId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.Payments.AsNoTracking().CountAsync(p => p.StripeSessionId == sessionId);
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
