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
/// Admin order workbench (Phase 3 Chunk 2) — the read side: the all-orders list + detail, with RBAC
/// (Orders.View = Staff/StoreManager/Administrator) and the customer-email / payment / shipment fields
/// the customer-facing order DTOs omit. Orders are seeded directly via the DbContext.
/// </summary>
[Collection("api")]
public class AdminOrderFlowTests
{
    private const int PriceCents = 2500;
    private readonly ApiFactory _factory;

    public AdminOrderFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListOrders_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/admin/orders")).StatusCode);
    }

    [Fact]
    public async Task ListOrders_Customer_Returns403()
    {
        (HttpClient customer, _) = await CustomerClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/admin/orders")).StatusCode);
    }

    [Fact]
    public async Task ListOrders_Staff_ReturnsSeededOrder()
    {
        string email = $"guest-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);

        (HttpClient staff, _) = await StaffClientAsync();
        JsonElement data = await DataAsync(await staff.GetAsync($"/api/v1/admin/orders?CustomerEmail={email}"));

        JsonElement order = data
            .GetProperty("items")
            .EnumerateArray()
            .Single(o => o.GetProperty("id").GetString() == orderId.ToString());
        Assert.Equal("Paid", order.GetProperty("status").GetString());
        Assert.Equal(email, order.GetProperty("customerEmail").GetString());
    }

    [Fact]
    public async Task GetOrder_Staff_ReturnsDetailWithPaymentAndEmail()
    {
        string email = $"guest-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);

        (HttpClient staff, _) = await StaffClientAsync();
        JsonElement data = await DataAsync(await staff.GetAsync($"/api/v1/admin/orders/{orderId}"));

        Assert.Equal(email, data.GetProperty("customerEmail").GetString());
        Assert.Equal("Paid", data.GetProperty("status").GetString());
        Assert.Single(data.GetProperty("payments").EnumerateArray());
        // An unshipped order has no shipment (null is omitted from the JSON).
        bool hasShipment = data.TryGetProperty("shipment", out JsonElement ship) && ship.ValueKind != JsonValueKind.Null;
        Assert.False(hasShipment);
    }

    [Fact]
    public async Task GetOrder_UnknownId_Returns404()
    {
        (HttpClient staff, _) = await StaffClientAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/admin/orders/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ListOrders_FiltersByStatus()
    {
        string email = $"guest-{Guid.NewGuid():N}@test.local";
        await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);
        (Guid pendingId, _) = await SeedOrderAsync(OrderStatus.Pending, email);

        (HttpClient staff, _) = await StaffClientAsync();
        JsonElement data = await DataAsync(
            await staff.GetAsync($"/api/v1/admin/orders?CustomerEmail={email}&Status=Pending"));

        JsonElement order = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal(pendingId.ToString(), order.GetProperty("id").GetString());
    }

    [Fact]
    public async Task MarkShipped_Staff_SetsFulfilledAndShipment_AndAudits()
    {
        string email = $"guest-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);

        (HttpClient staff, string csrf) = await StaffClientAsync();
        JsonElement data = await DataAsync(await PostJsonAsync(staff, $"/api/v1/admin/orders/{orderId}/ship",
            new { carrier = "AusPost", trackingNumber = "TRK123" }, csrf));

        Assert.Equal("Fulfilled", data.GetProperty("status").GetString());
        JsonElement shipment = data.GetProperty("shipment");
        Assert.Equal("Shipped", shipment.GetProperty("status").GetString());
        Assert.Equal("AusPost", shipment.GetProperty("carrier").GetString());
        Assert.True(await HasAuditRowAsync("Shipped", "Order", orderId.ToString()));
    }

    [Fact]
    public async Task MarkShipped_NonPaidOrder_Returns409()
    {
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Pending, $"g-{Guid.NewGuid():N}@test.local");
        (HttpClient staff, string csrf) = await StaffClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(staff, $"/api/v1/admin/orders/{orderId}/ship",
            new { carrier = "AusPost", trackingNumber = "TRK1" }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task MarkShipped_Customer_Returns403()
    {
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, $"g-{Guid.NewGuid():N}@test.local", withCharge: true);
        (HttpClient customer, string csrf) = await CustomerClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(customer, $"/api/v1/admin/orders/{orderId}/ship",
            new { carrier = "X", trackingNumber = "Y" }, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MarkDelivered_AfterShip_SetsDelivered()
    {
        string email = $"g-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);
        (HttpClient staff, string csrf) = await StaffClientAsync();
        (await PostJsonAsync(staff, $"/api/v1/admin/orders/{orderId}/ship",
            new { carrier = "AusPost", trackingNumber = "T1" }, csrf)).EnsureSuccessStatusCode();

        JsonElement data = await DataAsync(await PostAsync(staff, $"/api/v1/admin/orders/{orderId}/deliver", csrf));
        Assert.Equal("Delivered", data.GetProperty("shipment").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Refund_Manager_RefundsRestocksAndAudits()
    {
        string email = $"g-{Guid.NewGuid():N}@test.local";
        (Guid orderId, Guid variantId) = await SeedOrderAsync(OrderStatus.Paid, email, onHand: 4, withCharge: true);
        int before = await ReadOnHandAsync(variantId);

        (HttpClient manager, string csrf) = await StoreManagerClientAsync();
        JsonElement data = await DataAsync(await PostAsync(manager, $"/api/v1/admin/orders/{orderId}/refund", csrf));

        Assert.Equal("Refunded", data.GetProperty("status").GetString());
        Assert.Equal(before + 1, await ReadOnHandAsync(variantId)); // the one line unit is restocked
        Assert.Contains(data.GetProperty("payments").EnumerateArray(),
            payment => payment.GetProperty("amountCents").GetInt32() < 0); // a negative refund row
        Assert.True(await HasAuditRowAsync("Refund", "Order", orderId.ToString()));
    }

    [Fact]
    public async Task Refund_Staff_Returns403()
    {
        // Orders.Refund is StoreManager + Administrator only — Staff can fulfil but not refund.
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, $"g-{Guid.NewGuid():N}@test.local", withCharge: true);
        (HttpClient staff, string csrf) = await StaffClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden,
            (await PostAsync(staff, $"/api/v1/admin/orders/{orderId}/refund", csrf)).StatusCode);
    }

    [Fact]
    public async Task Refund_FulfilledOrder_Returns409()
    {
        // A shipped (Fulfilled) order is a return/RMA case — refunding it would restock goods already
        // with the customer. Phase 3 refunds Paid orders only.
        string email = $"g-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Paid, email, withCharge: true);
        (HttpClient staff, string staffCsrf) = await StaffClientAsync();
        (await PostJsonAsync(staff, $"/api/v1/admin/orders/{orderId}/ship",
            new { carrier = "AusPost", trackingNumber = "T1" }, staffCsrf)).EnsureSuccessStatusCode();

        (HttpClient manager, string csrf) = await StoreManagerClientAsync();
        Assert.Equal(HttpStatusCode.Conflict,
            (await PostAsync(manager, $"/api/v1/admin/orders/{orderId}/refund", csrf)).StatusCode);
    }

    [Fact]
    public async Task Refund_FromRefundingState_Recovers()
    {
        // A wedged Refunding order (a prior attempt refunded at Stripe but didn't finish the local
        // reversal) is re-drivable: the refund re-applies idempotently and completes to Refunded.
        string email = $"g-{Guid.NewGuid():N}@test.local";
        (Guid orderId, _) = await SeedOrderAsync(OrderStatus.Refunding, email, withCharge: true);

        (HttpClient manager, string csrf) = await StoreManagerClientAsync();
        JsonElement data = await DataAsync(await PostAsync(manager, $"/api/v1/admin/orders/{orderId}/refund", csrf));

        Assert.Equal("Refunded", data.GetProperty("status").GetString());
        Assert.True(await HasAuditRowAsync("Refund", "Order", orderId.ToString()));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(Guid OrderId, Guid VariantId)> SeedOrderAsync(
        OrderStatus status, string guestEmail, int onHand = 5, int quantity = 1, bool withCharge = false)
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
            GuestEmail = guestEmail,
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
        if (withCharge)
        {
            order.Payments.Add(new Payment
            {
                Provider = "stripe",
                StripePaymentIntentId = $"pi_test_{suffix}",
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

    private Task<(HttpClient Client, string Csrf)> StaffClientAsync() =>
        LoginAsync("staff@test.local", "TestStaff123456");

    private Task<(HttpClient Client, string Csrf)> StoreManagerClientAsync() =>
        LoginAsync("manager@test.local", "TestManager123456");

    private async Task<bool> HasAuditRowAsync(string action, string entityType, string entityId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.Action == action && a.EntityType == entityType && a.EntityId == entityId);
    }

    private async Task<int> ReadOnHandAsync(Guid variantId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return (await db.InventoryItems.AsNoTracking().FirstAsync(i => i.ProductVariantId == variantId)).OnHand;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

    private async Task<(HttpClient Client, string Csrf)> LoginAsync(string email, string password)
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login", new { email, password }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf"));
    }

    private async Task<(HttpClient Client, string Csrf)> CustomerClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();
        return (client, ExtractCookie(register, "csrf"));
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage resp)
    {
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
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
