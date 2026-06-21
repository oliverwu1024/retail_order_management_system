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
/// Order-anomaly Risk Queue (Phase 5B Chunk 3): GET/acknowledge under the Anomaly.Manage policy
/// (Staff + StoreManager + Administrator — Staff INCLUDED, unlike Chat.View/Sentiment.View), plus the
/// Mark-Shipped block (a flagged, unacknowledged order can't ship until acknowledged).
/// </summary>
[Collection("api")]
public class RiskQueueTests
{
    private readonly ApiFactory _factory;

    public RiskQueueTests(ApiFactory factory)
    {
        _factory = factory;
    }

    // ── Risk Queue read + RBAC ────────────────────────────────────────────────

    [Fact]
    public async Task ListAnomalies_AsStaff_IncludesSeededFlag()
    {
        (Guid orderId, _) = await SeedOrderAsync(withFlag: true);
        int orderNumber = await OrderNumberAsync(orderId);
        (HttpClient staff, _) = await LoginAsync("staff@test.local", "TestStaff123456");

        JsonElement data = await GetJsonAsync(staff, "/api/v1/analytics/anomalies?Page=1&PageSize=100");

        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("orderNumber").GetInt32() == orderNumber);
    }

    [Fact]
    public async Task ListAnomalies_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/analytics/anomalies")).StatusCode);
    }

    [Fact]
    public async Task ListAnomalies_AsCustomer_Returns403()
    {
        (HttpClient customer, _) = await RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/analytics/anomalies")).StatusCode);
    }

    [Fact]
    public async Task ListAnomalies_AsManager_Returns200()
    {
        (HttpClient manager, _) = await LoginAsync("manager@test.local", "TestManager123456");
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/v1/analytics/anomalies")).StatusCode);
    }

    // ── Acknowledge ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Acknowledge_RemovesFromQueue()
    {
        (Guid orderId, Guid anomalyId) = await SeedOrderAsync(withFlag: true);
        int orderNumber = await OrderNumberAsync(orderId);
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        (await PostJsonAsync(staff, $"/api/v1/analytics/anomalies/{anomalyId}/acknowledge", new { }, csrf))
            .EnsureSuccessStatusCode();

        JsonElement data = await GetJsonAsync(staff, "/api/v1/analytics/anomalies?Page=1&PageSize=100");
        Assert.DoesNotContain(
            data.GetProperty("items").EnumerateArray(),
            a => a.GetProperty("orderNumber").GetInt32() == orderNumber);
    }

    [Fact]
    public async Task Acknowledge_UnknownId_Returns404()
    {
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");
        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/analytics/anomalies/{Guid.NewGuid()}/acknowledge", new { }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── Mark-Shipped block ────────────────────────────────────────────────────

    [Fact]
    public async Task MarkShipped_FlaggedUnacknowledged_Returns409()
    {
        (Guid orderId, _) = await SeedOrderAsync(withFlag: true);
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/admin/orders/{orderId}/ship", new { carrier = "AusPost", trackingNumber = "T1" }, csrf);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task MarkShipped_UnscannedAnomalousOrder_EvaluatesOnShipAndReturns409()
    {
        // No anomaly row yet, but a quantity spike (qty 9 > 5) → evaluate-on-ship flags it → 409.
        (Guid orderId, _) = await SeedOrderAsync(quantity: 9);
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/admin/orders/{orderId}/ship", new { carrier = "AusPost", trackingNumber = "T1" }, csrf);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task MarkShipped_AfterAcknowledge_Ships()
    {
        (Guid orderId, Guid anomalyId) = await SeedOrderAsync(withFlag: true);
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        (await PostJsonAsync(staff, $"/api/v1/analytics/anomalies/{anomalyId}/acknowledge", new { }, csrf))
            .EnsureSuccessStatusCode();

        JsonElement data = await DataAsync(await PostJsonAsync(
            staff, $"/api/v1/admin/orders/{orderId}/ship", new { carrier = "AusPost", trackingNumber = "T1" }, csrf));

        Assert.Equal("Fulfilled", data.GetProperty("status").GetString());
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(Guid OrderId, Guid AnomalyId)> SeedOrderAsync(int quantity = 1, bool withFlag = false)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        DateTimeOffset now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        const int price = 2_500;

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
        var inventory = new InventoryItem { Variant = variant, OnHand = quantity + 5 };
        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        var order = new Order
        {
            GuestEmail = $"guest-{suffix}@test.local",
            Status = OrderStatus.Paid,
            SubtotalCents = price * quantity,
            TaxCents = 0,
            ShippingCents = 0,
            TotalCents = price * quantity,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = now,
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariant = variant,
            Quantity = quantity,
            UnitPriceCents = price,
            LineTotalCents = price * quantity,
            SkuSnapshot = variant.Sku,
            NameSnapshot = product.Name,
        });
        order.Payments.Add(new Payment
        {
            Provider = "stripe",
            StripePaymentIntentId = $"pi_{suffix}",
            AmountCents = price * quantity,
            Currency = "AUD",
            Status = PaymentStatus.Succeeded,
        });
        db.AddRange(category, product, variant, inventory);
        db.Orders.Add(order);

        var anomalyId = Guid.Empty;
        if (withFlag)
        {
            var anomaly = new OrderAnomaly
            {
                Order = order,
                Score = 5.0m,
                Reason = "Seeded anomaly",
                DetectedAt = now,
                Acknowledged = false,
            };
            db.OrderAnomalies.Add(anomaly);
            await db.SaveChangesAsync();
            anomalyId = anomaly.Id;
        }
        else
        {
            await db.SaveChangesAsync();
        }

        return (order.Id, anomalyId);
    }

    private async Task<int> OrderNumberAsync(Guid orderId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.Orders.Where(o => o.Id == orderId).Select(o => o.OrderNumber).SingleAsync();
    }

    private async Task<(HttpClient Client, string Csrf)> RegisterCustomerAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();
        return (client, ExtractCookie(register, "csrf"));
    }

    private async Task<(HttpClient Client, string Csrf)> LoginAsync(string email, string password)
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login", new { email, password }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf"));
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        HttpResponseMessage resp = await client.GetAsync(path);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
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
