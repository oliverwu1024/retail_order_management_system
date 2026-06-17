using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Admin inventory adjustment (Phase 3 Chunk 3): POST /api/v1/admin/inventory/{variantId}/adjust,
/// gated by Inventory.Adjust (Staff/StoreManager/Administrator). A tracked write, so it writes an
/// audit row — closing the Chunk-0 gap where set-based inventory writes weren't audited.
/// </summary>
[Collection("api")]
public class InventoryAdjustTests
{
    private readonly ApiFactory _factory;

    public InventoryAdjustTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Adjust_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage resp = await anon.PostAsJsonAsync(
            $"/api/v1/admin/inventory/{Guid.NewGuid()}/adjust", new { delta = 1, reason = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_Customer_Returns403()
    {
        (HttpClient customer, string csrf) = await CustomerClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(
            customer, $"/api/v1/admin/inventory/{Guid.NewGuid()}/adjust", new { delta = 1, reason = "x" }, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_Staff_IncreasesOnHand_AndAudits()
    {
        (Guid variantId, Guid itemId) = await SeedVariantAsync(onHand: 10);
        (HttpClient staff, string csrf) = await StaffClientAsync();

        JsonElement data = await DataAsync(await PostJsonAsync(
            staff, $"/api/v1/admin/inventory/{variantId}/adjust", new { delta = 5, reason = "Cycle count" }, csrf));

        Assert.Equal(15, data.GetProperty("onHand").GetInt32());
        Assert.True(await HasAuditRowAsync("InventoryAdjusted", "InventoryItem", itemId.ToString()));
    }

    [Fact]
    public async Task Adjust_BelowZero_Returns409()
    {
        (Guid variantId, _) = await SeedVariantAsync(onHand: 2);
        (HttpClient staff, string csrf) = await StaffClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/admin/inventory/{variantId}/adjust", new { delta = -5, reason = "Shrinkage" }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_ZeroDelta_Returns422()
    {
        (Guid variantId, _) = await SeedVariantAsync(onHand: 5);
        (HttpClient staff, string csrf) = await StaffClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/admin/inventory/{variantId}/adjust", new { delta = 0, reason = "noop" }, csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Adjust_BelowReserved_Returns409()
    {
        // OnHand 5, Reserved 5 (all promised to in-flight checkouts) → a −3 delta would leave
        // OnHand 2 < Reserved 5, i.e. Available −3. Must be rejected even though OnHand stays positive.
        (Guid variantId, _) = await SeedVariantAsync(onHand: 5, reserved: 5);
        (HttpClient staff, string csrf) = await StaffClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(
            staff, $"/api/v1/admin/inventory/{variantId}/adjust", new { delta = -3, reason = "Damage" }, csrf);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(Guid VariantId, Guid ItemId)> SeedVariantAsync(int onHand, int reserved = 0)
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
            PriceCents = 1999,
            IsActive = true,
        };
        var inventory = new InventoryItem { Variant = variant, OnHand = onHand, Reserved = reserved };

        db.AddRange(category, product, variant, inventory);
        await db.SaveChangesAsync();
        return (variant.Id, inventory.Id);
    }

    private async Task<bool> HasAuditRowAsync(string action, string entityType, string entityId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.AuditLogs.AsNoTracking()
            .AnyAsync(a => a.Action == action && a.EntityType == entityType && a.EntityId == entityId);
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
