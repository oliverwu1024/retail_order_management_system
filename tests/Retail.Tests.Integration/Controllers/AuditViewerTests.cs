using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Audit-log viewer (Phase 3 Chunk 3): the GET /api/v1/audit-logs search endpoint, gated by the
/// Audit.View policy (Staff/StoreManager/Administrator), and the end-to-end proof that the
/// AuditTrailInterceptor's rows are searchable — creating a product writes an Insert row the viewer
/// can find by entity.
/// </summary>
[Collection("api")]
public class AuditViewerTests
{
    private readonly ApiFactory _factory;

    public AuditViewerTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Search_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/audit-logs")).StatusCode);
    }

    [Fact]
    public async Task Search_Customer_Returns403()
    {
        (HttpClient customer, _) = await CustomerClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/audit-logs")).StatusCode);
    }

    [Fact]
    public async Task Search_Staff_Returns200()
    {
        // Audit.View admits Staff (read-only).
        (HttpClient staff, _) = await StaffClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/audit-logs?PageSize=5")).StatusCode);
    }

    [Fact]
    public async Task Search_ByEntity_FindsTheInsertRowForACreatedProduct()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        string productId = await CreateProductAsync(admin, csrf);

        (HttpClient staff, _) = await StaffClientAsync();
        JsonElement data = await DataAsync(
            await staff.GetAsync($"/api/v1/audit-logs?EntityType=Product&EntityId={productId}"));

        JsonElement row = Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("Insert", row.GetProperty("action").GetString());
        Assert.Equal("Product", row.GetProperty("entityType").GetString());
        Assert.NotEqual("system", row.GetProperty("actor").GetString()); // the authenticated admin
        Assert.False(string.IsNullOrEmpty(row.GetProperty("afterJson").GetString())); // captured the new values
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static async Task<string> CreateProductAsync(HttpClient admin, string csrf)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage categoryResp = await PostJsonAsync(admin, "/api/v1/catalog/categories",
            new { name = $"Cat {suffix}", slug = (string?)null, parentId = (Guid?)null }, csrf);
        categoryResp.EnsureSuccessStatusCode();
        string categoryId = (await DataAsync(categoryResp)).GetProperty("id").GetString()!;

        HttpResponseMessage productResp = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku = $"SKU-{suffix}", name = $"Product {suffix}", categoryId, isPublished = true }, csrf);
        productResp.EnsureSuccessStatusCode();
        return (await DataAsync(productResp)).GetProperty("id").GetString()!;
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
