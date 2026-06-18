using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Constants;
using Retail.Api.Domain.Entities;
using Retail.Api.Identity;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// RBAC matrix tests for the Phase 3 admin surface: the policy-based authorization
/// (<c>Roles.Policies.*</c>) correctly admits/denies Customer / Staff / StoreManager /
/// Administrator, and the StoreManager-can't-create-a-StoreManager in-handler rule holds.
/// Backs REQUIREMENTS Task 3.4.3 (a non-admin principal hitting an admin endpoint → 403).
/// </summary>
[Collection("api")]
public class AdminAccessControlTests
{
    private readonly ApiFactory _factory;

    public AdminAccessControlTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdminUsers_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        HttpResponseMessage resp = await anon.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_Customer_Returns403()
    {
        (HttpClient customer, _) = await CustomerClientAsync();
        HttpResponseMessage resp = await customer.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_ForgedCustomerJwt_Returns403()
    {
        // Task 3.4.3: a VALIDLY-SIGNED bearer carrying only the Customer role must be rejected by
        // AUTHORIZATION (403), not merely authentication — the admin policies gate on role, not just
        // on a good signature. Minted directly via IJwtService (no login flow), so it's a genuinely
        // forged token rather than a real session cookie.
        string token;
        using (IServiceScope scope = _factory.Services.CreateScope())
        {
            IJwtService jwt = scope.ServiceProvider.GetRequiredService<IJwtService>();
            var customer = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(),
                Email = "forged@test.local",
                DisplayName = "Forged Customer",
            };
            (token, _) = jwt.CreateAccessToken(customer, new[] { Roles.Customer });
        }

        HttpClient client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/users");
        request.Headers.Add("Cookie", $"{AuthConstants.AccessTokenCookie}={token}");
        HttpResponseMessage resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_Staff_Returns403()
    {
        // Staff is NOT in Users.ManageStaff (= StoreManager + Administrator).
        (HttpClient staff, _) = await StaffClientAsync();
        HttpResponseMessage resp = await staff.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_StoreManager_Returns200()
    {
        (HttpClient manager, _) = await StoreManagerClientAsync();
        HttpResponseMessage resp = await manager.GetAsync("/api/v1/admin/users");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task CreateStaff_ByStoreManager_Returns201()
    {
        (HttpClient manager, string csrf) = await StoreManagerClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(manager, "/api/v1/admin/users", NewUser("Staff"), csrf);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task CreateStoreManager_ByStoreManager_Returns403()
    {
        // The in-handler rule: a StoreManager may create Staff, but only an Administrator may
        // create another StoreManager.
        (HttpClient manager, string csrf) = await StoreManagerClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(manager, "/api/v1/admin/users", NewUser("StoreManager"), csrf);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task CreateStoreManager_ByAdmin_Returns201()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(admin, "/api/v1/admin/users", NewUser("StoreManager"), csrf);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }

    [Fact]
    public async Task CreateUser_DisallowedRole_Returns422()
    {
        (HttpClient admin, string csrf) = await AdminClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(
            admin, "/api/v1/admin/users", NewUser("Customer"), csrf); // Customer is not creatable here
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task CatalogWrite_ByStaff_Returns403()
    {
        // Catalog writes migrated from [Authorize(Roles=Administrator)] to Policy=Catalog.Manage
        // (Administrator only) — Staff must still be denied (authz short-circuits before the body matters).
        (HttpClient staff, string csrf) = await StaffClientAsync();
        HttpResponseMessage resp = await PostJsonAsync(staff, "/api/v1/catalog/products",
            new { sku = "X", name = "X", categoryId = Guid.NewGuid().ToString(), isPublished = false }, csrf);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static object NewUser(string role) => new
    {
        email = $"u-{Guid.NewGuid():N}@test.local",
        password = "Sup3rSecret!pw",
        displayName = "New User",
        role,
    };

    private Task<(HttpClient Client, string Csrf)> AdminClientAsync() =>
        LoginAsync("admin@test.local", "TestAdmin123456");

    private Task<(HttpClient Client, string Csrf)> StaffClientAsync() =>
        LoginAsync("staff@test.local", "TestStaff123456");

    private Task<(HttpClient Client, string Csrf)> StoreManagerClientAsync() =>
        LoginAsync("manager@test.local", "TestManager123456");

    private async Task<(HttpClient Client, string Csrf)> LoginAsync(string email, string password)
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login", new { email, password }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf")); // login re-issues the csrf token
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
