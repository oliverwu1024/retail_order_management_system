using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Customer order viewing + cancellation tests on real SQL Server (Story 2.4): the order list +
/// detail are scoped to the caller, a guest can look up an order by its Stripe session id, and a
/// customer can cancel a paid order (refund faked, stock restocked). Orders are seeded via the
/// DbContext for a focused setup.
/// </summary>
[Collection("api")]
public class OrderViewingTests
{
    private const int PriceCents = 1999;
    private readonly ApiFactory _factory;

    public OrderViewingTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task MyOrders_ListAndDetail_AreScopedToCaller()
    {
        (HttpClient client, _, Guid profileId) = await RegisterCustomerAsync();
        (Guid order1, _) = await SeedOrderAsync(profileId, status: OrderStatus.Paid);
        (Guid order2, _) = await SeedOrderAsync(profileId, status: OrderStatus.Paid);

        // An order belonging to a DIFFERENT customer.
        (_, _, Guid otherProfileId) = await RegisterCustomerAsync();
        (Guid foreignOrder, _) = await SeedOrderAsync(otherProfileId, status: OrderStatus.Paid);

        JsonElement list = (await (await client.GetAsync("/api/v1/orders")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        List<string?> ids = list.GetProperty("items").EnumerateArray()
            .Select(o => o.GetProperty("id").GetString()).ToList();
        Assert.Equal(2, ids.Count); // only the caller's two orders
        Assert.Contains(order1.ToString(), ids);
        Assert.Contains(order2.ToString(), ids);

        // Detail of an owned order; a foreign order is a 404 (never confirmed to exist).
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/orders/{order1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/orders/{foreignOrder}")).StatusCode);
    }

    [Fact]
    public async Task GuestOrderBySession_ReturnsTheOrder()
    {
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        (Guid orderId, _) = await SeedOrderAsync(profileId: null, status: OrderStatus.Paid, sessionId: sessionId);

        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage resp = await anon.GetAsync($"/api/v1/orders/by-session/{sessionId}");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(orderId.ToString(), data.GetProperty("id").GetString());
    }

    [Fact]
    public async Task GuestOrderBySession_DoesNotExposeMemberOrders()
    {
        // A MEMBER order also carries a Payment with the Stripe session id, but the anonymous
        // by-session path is for GUEST orders only — returning a member's PII-bearing order to an
        // unauthenticated caller would be an IDOR. Expect 404, not 200.
        (_, _, Guid profileId) = await RegisterCustomerAsync();
        string sessionId = $"cs_test_{Guid.NewGuid():N}";
        await SeedOrderAsync(profileId, status: OrderStatus.Paid, sessionId: sessionId);

        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage resp = await anon.GetAsync($"/api/v1/orders/by-session/{sessionId}");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task CancelPaidOrder_RefundsAndRestocks()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync();
        (Guid orderId, Guid variantId) = await SeedOrderAsync(
            profileId, status: OrderStatus.Paid, onHand: 3, quantity: 2, withCharge: true);

        HttpResponseMessage resp = await PostJsonAsync(client, $"/api/v1/orders/{orderId}/cancel", new { }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("Refunded", data.GetProperty("status").GetString());
        Assert.Equal(5, await ReadOnHandAsync(variantId)); // 3 + 2 restocked
    }

    [Fact]
    public async Task CancelNonPaidOrder_Returns409()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync();
        (Guid orderId, _) = await SeedOrderAsync(profileId, status: OrderStatus.Refunded, withCharge: true);

        HttpResponseMessage resp = await PostJsonAsync(client, $"/api/v1/orders/{orderId}/cancel", new { }, csrf);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task ConcurrentCancel_RefundsOnce_RestocksOnce_AndLoserGets409()
    {
        // Two simultaneous cancels on the same paid order. The Paid → Refunding claim must let
        // exactly one win, so Stripe is hit once, stock is restocked once, and the loser gets 409.
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync();
        string paymentIntentId = $"pi_test_{Guid.NewGuid():N}";
        (Guid orderId, Guid variantId) = await SeedOrderAsync(
            profileId, status: OrderStatus.Paid, onHand: 3, quantity: 2, paymentIntentId: paymentIntentId);

        HttpResponseMessage[] responses = await Task.WhenAll(
            PostJsonAsync(client, $"/api/v1/orders/{orderId}/cancel", new { }, csrf),
            PostJsonAsync(client, $"/api/v1/orders/{orderId}/cancel", new { }, csrf));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));        // exactly one winner
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));  // loser gets a clean 409
        Assert.Equal(1, FakeStripeRefundGateway.RefundCountFor(paymentIntentId));        // refunded once, not twice
        Assert.Equal(5, await ReadOnHandAsync(variantId));                               // 3 + 2 restocked exactly once
    }

    [Fact]
    public async Task ListMyOrders_AcceptsPascalCaseAndLowercasePaging()
    {
        (HttpClient client, _, Guid profileId) = await RegisterCustomerAsync();
        await SeedOrderAsync(profileId, status: OrderStatus.Paid);
        await SeedOrderAsync(profileId, status: OrderStatus.Paid);
        await SeedOrderAsync(profileId, status: OrderStatus.Paid);

        // PascalCase (the documented DTO contract) bounds the page to 2 items...
        JsonElement pascal = (await (await client.GetAsync("/api/v1/orders?Page=1&PageSize=2"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(2, pascal.GetProperty("items").GetArrayLength());
        Assert.Equal(3, pascal.GetProperty("totalCount").GetInt32());

        // ...and lowercase still binds (query-string binding is case-insensitive) → no hard break.
        JsonElement lower = (await (await client.GetAsync("/api/v1/orders?page=1&pageSize=2"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(2, lower.GetProperty("items").GetArrayLength());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string Csrf, Guid ProfileId)> RegisterCustomerAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();
        csrf = ExtractCookie(register, "csrf");

        // GET the (lazily-created) profile to learn its id, which seeded orders are keyed to.
        JsonElement profile = (await (await client.GetAsync("/api/v1/profile")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        return (client, csrf, Guid.Parse(profile.GetProperty("id").GetString()!));
    }

    private async Task<(Guid OrderId, Guid VariantId)> SeedOrderAsync(
        Guid? profileId,
        OrderStatus status,
        int onHand = 5,
        int quantity = 1,
        bool withCharge = false,
        string? sessionId = null,
        string? paymentIntentId = null)
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

        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        var order = new Order
        {
            CustomerProfileId = profileId,
            GuestEmail = profileId is null ? "guest@test.local" : null,
            Status = status,
            SubtotalCents = PriceCents * quantity,
            TaxCents = 0,
            ShippingCents = 0,
            TotalCents = PriceCents * quantity,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariant = variant,
            Quantity = quantity,
            UnitPriceCents = PriceCents,
            LineTotalCents = PriceCents * quantity,
            SkuSnapshot = variant.Sku,
            NameSnapshot = product.Name,
        });
        if (withCharge || sessionId is not null || paymentIntentId is not null)
        {
            order.Payments.Add(new Payment
            {
                Provider = "stripe",
                StripeSessionId = sessionId,
                StripePaymentIntentId = paymentIntentId ?? (withCharge ? $"pi_test_{suffix}" : null),
                AmountCents = PriceCents * quantity,
                Currency = "AUD",
                Status = PaymentStatus.Succeeded,
            });
        }

        db.AddRange(category, product, variant, inventory);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return (order.Id, variant.Id);
    }

    private async Task<int> ReadOnHandAsync(Guid variantId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return (await db.InventoryItems.AsNoTracking().FirstAsync(i => i.ProductVariantId == variantId)).OnHand;
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

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
