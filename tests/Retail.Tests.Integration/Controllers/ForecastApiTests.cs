using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Forecast + reorder-hint API (Phase 5B forecasting Chunk 3): GET /analytics/forecast +
/// /analytics/reorder-hints + POST dismiss, gated by Forecast.View (Staff + StoreManager +
/// Administrator). Rows are seeded directly (the refresh service is C2).
/// </summary>
[Collection("api")]
public class ForecastApiTests
{
    private readonly ApiFactory _factory;

    public ForecastApiTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListForecasts_AsStaff_IncludesSeededVariant()
    {
        (string sku, _) = await SeedForecastAsync();
        (HttpClient staff, _) = await LoginAsync("staff@test.local", "TestStaff123456");

        JsonElement data = await GetJsonAsync(staff, "/api/v1/analytics/forecast?Page=1&PageSize=100");

        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            f => f.GetProperty("sku").GetString() == sku);
    }

    [Fact]
    public async Task ListForecasts_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/analytics/forecast")).StatusCode);
    }

    [Fact]
    public async Task ListReorderHints_AsCustomer_Returns403()
    {
        (HttpClient customer, _) = await RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/analytics/reorder-hints")).StatusCode);
    }

    [Fact]
    public async Task ListReorderHints_AsManager_IncludesSeededHint()
    {
        (string sku, _) = await SeedForecastAsync();
        (HttpClient manager, _) = await LoginAsync("manager@test.local", "TestManager123456");

        JsonElement data = await GetJsonAsync(manager, "/api/v1/analytics/reorder-hints?Page=1&PageSize=100");

        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            h => h.GetProperty("sku").GetString() == sku);
    }

    [Fact]
    public async Task DismissReorderHint_RemovesItFromTheList()
    {
        (string sku, Guid hintId) = await SeedForecastAsync();
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        (await PostJsonAsync(staff, $"/api/v1/analytics/reorder-hints/{hintId}/dismiss", new { }, csrf))
            .EnsureSuccessStatusCode();

        JsonElement data = await GetJsonAsync(staff, "/api/v1/analytics/reorder-hints?Page=1&PageSize=100");
        Assert.DoesNotContain(
            data.GetProperty("items").EnumerateArray(),
            h => h.GetProperty("sku").GetString() == sku);
    }

    [Fact]
    public async Task DismissReorderHint_UnknownId_Returns404()
    {
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");
        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/analytics/reorder-hints/{Guid.NewGuid()}/dismiss", new { }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(string Sku, Guid HintId)> SeedForecastAsync()
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
            PriceCents = 2_000,
            IsActive = true,
        };
        db.AddRange(category, product, variant);

        db.DemandForecasts.Add(new DemandForecast
        {
            ProductVariant = variant,
            Horizon = 14,
            ForecastedQty = 56m,
            LowerBound = 0m,
            UpperBound = 120m,
            Confidence = 0.9m,
            ModelVersion = "2026-06-22",
            GeneratedAt = now,
        });
        var hint = new ReorderHint
        {
            ProductVariant = variant,
            RecommendedOrderQty = 40,
            Reasoning = "Seeded reorder hint",
            GeneratedAt = now,
            Dismissed = false,
        };
        db.ReorderHints.Add(hint);

        await db.SaveChangesAsync();
        return (variant.Sku, hint.Id);
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
