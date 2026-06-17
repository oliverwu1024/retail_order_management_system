using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Sales-by-day report (Phase 3 Chunk 3): GET /api/v1/analytics/sales-by-day, gated by Reports.View
/// (Staff/StoreManager/Administrator). Orders are seeded in a far-future date range so the per-day
/// and per-category aggregates are deterministic (isolated from other tests' now-dated orders).
/// </summary>
[Collection("api")]
public class ReportsTests
{
    private readonly ApiFactory _factory;

    public ReportsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SalesByDay_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/analytics/sales-by-day")).StatusCode);
    }

    [Fact]
    public async Task SalesByDay_Customer_Returns403()
    {
        (HttpClient customer, _) = await CustomerClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/analytics/sales-by-day")).StatusCode);
    }

    [Fact]
    public async Task SalesByDay_Staff_AggregatesByDayAndCategory()
    {
        string suffix = Guid.NewGuid().ToString("N")[..6];
        string catA = $"RPT-A-{suffix}";
        string catB = $"RPT-B-{suffix}";
        await SeedPaidOrderAsync(new DateTimeOffset(2099, 6, 1, 10, 0, 0, TimeSpan.Zero), catA, 2500);
        await SeedPaidOrderAsync(new DateTimeOffset(2099, 6, 2, 10, 0, 0, TimeSpan.Zero), catB, 3000);

        (HttpClient staff, _) = await StaffClientAsync();
        JsonElement data = await DataAsync(
            await staff.GetAsync("/api/v1/analytics/sales-by-day?From=2099-06-01T00:00:00Z&To=2099-06-03T00:00:00Z"));

        List<JsonElement> days = data.GetProperty("days").EnumerateArray().ToList();
        JsonElement day1 = days.Single(d => d.GetProperty("date").GetString() == "2099-06-01");
        Assert.Equal(1, day1.GetProperty("orderCount").GetInt32());
        Assert.Equal(2500, day1.GetProperty("totalSalesCents").GetInt64());
        JsonElement day2 = days.Single(d => d.GetProperty("date").GetString() == "2099-06-02");
        Assert.Equal(3000, day2.GetProperty("totalSalesCents").GetInt64());

        List<JsonElement> categories = data.GetProperty("categories").EnumerateArray().ToList();
        Assert.Equal(2500, categories.Single(c => c.GetProperty("category").GetString() == catA).GetProperty("totalSalesCents").GetInt64());
        Assert.Equal(3000, categories.Single(c => c.GetProperty("category").GetString() == catB).GetProperty("totalSalesCents").GetInt64());
    }

    [Fact]
    public async Task SalesByDay_ReversedRange_Returns422()
    {
        (HttpClient staff, _) = await StaffClientAsync();
        HttpResponseMessage resp = await staff.GetAsync(
            "/api/v1/analytics/sales-by-day?From=2099-06-10T00:00:00Z&To=2099-06-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task SalesByDay_RangeTooWide_Returns422()
    {
        (HttpClient staff, _) = await StaffClientAsync();
        HttpResponseMessage resp = await staff.GetAsync(
            "/api/v1/analytics/sales-by-day?From=2090-01-01T00:00:00Z&To=2099-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task SeedPaidOrderAsync(DateTimeOffset placedAt, string categoryName, int priceCents)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var category = new Category { Name = categoryName, Slug = $"cat-{suffix}" };
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
            PriceCents = priceCents,
            IsActive = true,
        };
        var inventory = new InventoryItem { Variant = variant, OnHand = 5 };
        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        var order = new Order
        {
            GuestEmail = $"g-{suffix}@test.local",
            Status = OrderStatus.Paid,
            SubtotalCents = priceCents,
            TaxCents = 0,
            ShippingCents = 0,
            TotalCents = priceCents,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = placedAt,
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariant = variant,
            Quantity = 1,
            UnitPriceCents = priceCents,
            LineTotalCents = priceCents,
            SkuSnapshot = variant.Sku,
            NameSnapshot = product.Name,
        });

        db.AddRange(category, product, variant, inventory);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
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
