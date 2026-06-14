using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// End-to-end customer profile + address tests on real SQL Server: lazy profile
/// creation, profile edit (Email immutable), address CRUD, the "one default per axis"
/// invariant (backed by a filtered unique index), ownership 404s, and the Customer-only
/// role gate.
/// </summary>
[Collection("api")]
public class CustomerProfileFlowTests
{
    private readonly ApiFactory _factory;

    public CustomerProfileFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetProfile_LazilyCreatesIt_ThenUpdateAppliesButEmailIsImmutable()
    {
        (HttpClient customer, string csrf, string email) = await CustomerClientAsync();

        // First GET lazily creates the profile, seeded from the registration display name.
        JsonElement created = await GetDataAsync(customer, "/api/v1/profile");
        Assert.Equal(email, created.GetProperty("email").GetString());
        Assert.Equal("Cust", created.GetProperty("displayName").GetString());
        Assert.Empty(created.GetProperty("addresses").EnumerateArray());

        // Update DisplayName + Phone.
        HttpResponseMessage updateResp = await PutJsonAsync(customer, "/api/v1/profile",
            new { displayName = "Updated Name", phone = "+61 400 000 000" }, csrf);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        JsonElement reread = await GetDataAsync(customer, "/api/v1/profile");
        Assert.Equal("Updated Name", reread.GetProperty("displayName").GetString());
        Assert.Equal("+61 400 000 000", reread.GetProperty("phone").GetString());
        // Email is immutable in the MVP — still the registration email.
        Assert.Equal(email, reread.GetProperty("email").GetString());
    }

    [Fact]
    public async Task AddAddress_ListsIt_ThenUpdateApplies()
    {
        (HttpClient customer, string csrf, _) = await CustomerClientAsync();

        HttpResponseMessage addResp = await PostJsonAsync(customer, "/api/v1/profile/addresses",
            ValidAddress(line1: "1 Test St", city: "Sydney", country: "au"), csrf);
        Assert.Equal(HttpStatusCode.Created, addResp.StatusCode);
        JsonElement created = (await addResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        string addressId = created.GetProperty("id").GetString()!;
        // Country is normalised to upper-case ISO-3166 alpha-2.
        Assert.Equal("AU", created.GetProperty("country").GetString());

        JsonElement list = await GetDataAsync(customer, "/api/v1/profile/addresses");
        Assert.Contains(list.EnumerateArray(), a => a.GetProperty("id").GetString() == addressId);

        HttpResponseMessage updateResp = await PutJsonAsync(customer, $"/api/v1/profile/addresses/{addressId}",
            ValidAddress(line1: "2 New Rd", city: "Melbourne", country: "AU"), csrf);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        JsonElement updated = (await updateResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal("2 New Rd", updated.GetProperty("line1").GetString());
        Assert.Equal("Melbourne", updated.GetProperty("city").GetString());
    }

    [Fact]
    public async Task SettingNewDefaultShipping_ClearsThePreviousDefault()
    {
        (HttpClient customer, string csrf, _) = await CustomerClientAsync();

        string firstId = await AddAddressAsync(customer, csrf,
            ValidAddress(line1: "1 First St", city: "Sydney", country: "AU", isDefaultShipping: true));
        string secondId = await AddAddressAsync(customer, csrf,
            ValidAddress(line1: "2 Second St", city: "Perth", country: "AU", isDefaultShipping: true));

        JsonElement list = await GetDataAsync(customer, "/api/v1/profile/addresses");
        Dictionary<string, bool> defaultShippingById = list.EnumerateArray()
            .ToDictionary(a => a.GetProperty("id").GetString()!, a => a.GetProperty("isDefaultShipping").GetBoolean());

        // The second address stole the default; the first was cleared — exactly one default.
        Assert.False(defaultShippingById[firstId]);
        Assert.True(defaultShippingById[secondId]);
        Assert.Single(defaultShippingById.Values, isDefault => isDefault);
    }

    [Fact]
    public async Task UpdateAddress_NotOwnedByCaller_Returns404()
    {
        (HttpClient owner, string ownerCsrf, _) = await CustomerClientAsync();
        string addressId = await AddAddressAsync(owner, ownerCsrf,
            ValidAddress(line1: "1 Owner St", city: "Sydney", country: "AU"));

        // A different customer tries to update it — valid body, so it reaches the
        // ownership check and 404s (we never confirm someone else's address id exists).
        (HttpClient intruder, string intruderCsrf, _) = await CustomerClientAsync();
        HttpResponseMessage resp = await PutJsonAsync(intruder, $"/api/v1/profile/addresses/{addressId}",
            ValidAddress(line1: "hijack", city: "Nowhere", country: "AU"), intruderCsrf);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddAddress_InvalidBody_Returns422()
    {
        (HttpClient customer, string csrf, _) = await CustomerClientAsync();

        // Blank Line1 + 3-letter (non alpha-2) country.
        HttpResponseMessage resp = await PostJsonAsync(customer, "/api/v1/profile/addresses",
            ValidAddress(line1: "", city: "Sydney", country: "AUS"), csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task GetProfile_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();

        HttpResponseMessage resp = await anon.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task GetProfile_AsAdmin_Returns403()
    {
        // Admin has the Administrator role, not Customer — the profile area is Customer-only.
        (HttpClient admin, _) = await AdminClientAsync();

        HttpResponseMessage resp = await admin.GetAsync("/api/v1/profile");

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    // A valid address body; override the fields a test wants to vary.
    private static object ValidAddress(
        string line1 = "1 Example St",
        string city = "Sydney",
        string country = "AU",
        bool isDefaultShipping = false,
        bool isDefaultBilling = false) =>
        new
        {
            line1,
            line2 = (string?)null,
            city,
            region = (string?)"NSW",
            postalCode = "2000",
            country,
            isDefaultShipping,
            isDefaultBilling,
        };

    private static async Task<string> AddAddressAsync(HttpClient client, string csrf, object body)
    {
        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/profile/addresses", body, csrf);
        resp.EnsureSuccessStatusCode();
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        return data.GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetDataAsync(HttpClient client, string path)
    {
        HttpResponseMessage resp = await client.GetAsync(path);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private async Task<(HttpClient Client, string Csrf)> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login",
            new { email = "admin@test.local", password = "TestAdmin123456" }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf"));
    }

    private async Task<(HttpClient Client, string Csrf, string Email)> CustomerClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        string email = $"cust-{Guid.NewGuid():N}@test.local";
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email, password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();
        return (client, ExtractCookie(register, "csrf"), email);
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Post, path, body, csrf);

    private static Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Put, path, body, csrf);

    private static Task<HttpResponseMessage> SendJsonAsync(HttpClient client, HttpMethod method, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body) };
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
