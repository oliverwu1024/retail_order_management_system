using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
