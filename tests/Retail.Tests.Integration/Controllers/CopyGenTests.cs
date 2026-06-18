using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// AI copy generation (Phase 4, Story 4.2): POST /api/v1/catalog/products/{id}/generate-copy, gated
/// by Catalog.Manage (Administrator-only). Runs against the hermetic StubLlmClient (Ai:Mode defaults
/// to "stub"), so no key/network is needed: Admin gets structured copy, Staff is forbidden, unknown
/// products 404, and bad input 422.
/// </summary>
[Collection("api")]
public class CopyGenTests
{
    private readonly ApiFactory _factory;

    public CopyGenTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GenerateCopy_AsAdmin_ReturnsStructuredCopy()
    {
        Guid productId = await SeedProductAsync();
        (HttpClient admin, string csrf) = await AdminClientAsync();

        HttpResponseMessage resp = await PostJsonAsync(admin,
            $"/api/v1/catalog/products/{productId}/generate-copy", new { tone = "professional", length = "medium" }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("description").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("seoTitle").GetString()));
        Assert.True(data.GetProperty("bulletPoints").GetArrayLength() > 0);
    }

    [Fact]
    public async Task GenerateCopy_AsStaff_Returns403()
    {
        Guid productId = await SeedProductAsync();
        (HttpClient staff, string csrf) = await StaffClientAsync();

        HttpResponseMessage resp = await PostJsonAsync(staff,
            $"/api/v1/catalog/products/{productId}/generate-copy", new { tone = "professional", length = "medium" }, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateCopy_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage resp = await anon.PostAsJsonAsync(
            $"/api/v1/catalog/products/{Guid.NewGuid()}/generate-copy", new { tone = "professional", length = "medium" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateCopy_UnknownProduct_Returns404()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(admin,
            $"/api/v1/catalog/products/{Guid.NewGuid()}/generate-copy", new { tone = "professional", length = "medium" }, csrf);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GenerateCopy_InvalidTone_Returns422()
    {
        Guid productId = await SeedProductAsync();
        (HttpClient admin, string csrf) = await AdminClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(admin,
            $"/api/v1/catalog/products/{productId}/generate-copy", new { tone = "sarcastic", length = "medium" }, csrf);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedProductAsync()
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
            BrandName = "Acme",
            IsPublished = true,
        };

        db.AddRange(category, product);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private Task<(HttpClient Client, string Csrf)> AdminClientAsync() =>
        LoginAsync("admin@test.local", "TestAdmin123456");

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
