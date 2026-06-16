using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// End-to-end cart tests through the full pipeline on real SQL Server (Story 2.1): guest
/// carts via the <c>anon_cart_key</c> cookie, dedup-on-add, quantity update, idempotent
/// remove, clear, validation/not-found mapping, guest isolation, and the guest→member
/// merge that fires on the first cart touch after login.
/// </summary>
[Collection("api")]
public class CartFlowTests
{
    private readonly ApiFactory _factory;

    public CartFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GuestAddsItem_CartReflectsIt_AndAnonCookieIssued()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, int priceCents) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guest, string csrf) = await GuestClientAsync();

        // Empty before anything is added (no cart row, no cookie spam).
        JsonElement before = await GetCartDataAsync(guest);
        Assert.Empty(before.GetProperty("items").EnumerateArray());

        HttpResponseMessage addResp = await AddItemAsync(guest, csrf, variantId, 2);
        Assert.Equal(HttpStatusCode.OK, addResp.StatusCode);

        // The guest cart is identified by a freshly-issued anon cookie.
        Assert.NotEmpty(ExtractCookie(addResp, "anon_cart_key"));

        JsonElement cart = (await addResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        JsonElement line = cart.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(variantId, line.GetProperty("productVariantId").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
        Assert.Equal(priceCents, line.GetProperty("unitPriceCents").GetInt32());
        Assert.Equal(priceCents * 2, line.GetProperty("lineTotalCents").GetInt32());
        Assert.Equal(priceCents * 2, cart.GetProperty("subtotalCents").GetInt32());
        Assert.Equal(2, cart.GetProperty("totalQuantity").GetInt32());

        // The cookie flows on the next request, so the cart persists for this guest.
        JsonElement reread = await GetCartDataAsync(guest);
        Assert.Single(reread.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AddingSameVariantTwice_BumpsExistingLine()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guest, string csrf) = await GuestClientAsync();
        await AddItemAsync(guest, csrf, variantId, 2);
        await AddItemAsync(guest, csrf, variantId, 3);

        JsonElement cart = await GetCartDataAsync(guest);
        JsonElement line = cart.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(5, line.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task UpdateQuantity_SetsAbsoluteValue()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guest, string csrf) = await GuestClientAsync();
        await AddItemAsync(guest, csrf, variantId, 2);

        HttpResponseMessage updateResp = await PutJsonAsync(guest, $"/api/v1/cart/items/{variantId}", new { quantity = 5 }, csrf);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);

        JsonElement cart = await GetCartDataAsync(guest);
        Assert.Equal(5, cart.GetProperty("items").EnumerateArray().Single().GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task RemoveItem_RemovesLine_AndIsIdempotent()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guest, string csrf) = await GuestClientAsync();
        await AddItemAsync(guest, csrf, variantId, 2);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(guest, $"/api/v1/cart/items/{variantId}", csrf)).StatusCode);
        Assert.Empty((await GetCartDataAsync(guest)).GetProperty("items").EnumerateArray());

        // Removing a line that's already gone is still a 200 (idempotent), not a 404.
        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(guest, $"/api/v1/cart/items/{variantId}", csrf)).StatusCode);
    }

    [Fact]
    public async Task ClearCart_EmptiesAllLines()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantA, _) = await CreateSellableVariantAsync(admin, adminCsrf, $"{suffix}a");
        (string variantB, _) = await CreateSellableVariantAsync(admin, adminCsrf, $"{suffix}b");

        (HttpClient guest, string csrf) = await GuestClientAsync();
        await AddItemAsync(guest, csrf, variantA, 1);
        await AddItemAsync(guest, csrf, variantB, 1);

        Assert.Equal(HttpStatusCode.OK, (await DeleteAsync(guest, "/api/v1/cart", csrf)).StatusCode);
        Assert.Empty((await GetCartDataAsync(guest)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task AddUnknownVariant_Returns404()
    {
        (HttpClient guest, string csrf) = await GuestClientAsync();

        HttpResponseMessage resp = await AddItemAsync(guest, csrf, Guid.NewGuid().ToString(), 1);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task AddInvalidQuantity_Returns422()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guest, string csrf) = await GuestClientAsync();

        HttpResponseMessage resp = await AddItemAsync(guest, csrf, variantId, 0);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task GuestCart_IsIsolatedFromOtherGuests()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        (HttpClient guestA, string csrfA) = await GuestClientAsync();
        await AddItemAsync(guestA, csrfA, variantId, 1);

        // A different browser (separate cookie jar) has its own, empty cart.
        (HttpClient guestB, _) = await GuestClientAsync();
        Assert.Empty((await GetCartDataAsync(guestB)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task GuestCartMergesIntoMemberCartOnLogin()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        (string variantId, _) = await CreateSellableVariantAsync(admin, adminCsrf, suffix);

        // One HttpClient == one browser (one cookie jar). Add as a guest, THEN register on
        // the same client so the anon cookie is still present alongside the new auth cookie.
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        await AddItemAsync(client, csrf, variantId, 2);

        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName = "Cust" }, csrf);
        register.EnsureSuccessStatusCode();

        // The first cart touch after login folds the guest cart into the member cart.
        JsonElement cart = await GetCartDataAsync(client);
        JsonElement line = cart.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(variantId, line.GetProperty("productVariantId").GetString());
        Assert.Equal(2, line.GetProperty("quantity").GetInt32());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string Csrf)> AdminClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage login = await PostJsonAsync(client, "/api/v1/auth/login",
            new { email = "admin@test.local", password = "TestAdmin123456" }, csrf);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf")); // login re-issues the csrf token
    }

    private async Task<(HttpClient Client, string Csrf)> GuestClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        return (client, csrf);
    }

    // Creates a published product with one active, in-stock variant and returns the variant
    // id + its price — the minimum needed to add something sellable to a cart.
    private static async Task<(string VariantId, int PriceCents)> CreateSellableVariantAsync(
        HttpClient admin, string csrf, string suffix, int stock = 10)
    {
        HttpResponseMessage categoryResp = await PostJsonAsync(admin, "/api/v1/catalog/categories",
            new { name = $"Cat {suffix}", slug = (string?)null, parentId = (Guid?)null }, csrf);
        categoryResp.EnsureSuccessStatusCode();
        string categoryId = (await categoryResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        HttpResponseMessage productResp = await PostJsonAsync(admin, "/api/v1/catalog/products",
            new { sku = $"SKU-{suffix}", name = $"Product {suffix}", categoryId, isPublished = true }, csrf);
        productResp.EnsureSuccessStatusCode();
        string productId = (await productResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        const int priceCents = 1999;
        HttpResponseMessage variantResp = await PostJsonAsync(admin, $"/api/v1/catalog/products/{productId}/variants",
            new
            {
                sku = $"VAR-{suffix}",
                options = new Dictionary<string, string> { ["size"] = "M" },
                priceCents,
                compareAtPriceCents = (int?)null,
                initialStock = stock,
            }, csrf);
        variantResp.EnsureSuccessStatusCode();
        string variantId = (await variantResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!;

        return (variantId, priceCents);
    }

    private static Task<HttpResponseMessage> AddItemAsync(HttpClient client, string csrf, string variantId, int quantity) =>
        PostJsonAsync(client, "/api/v1/cart/items", new { productVariantId = variantId, quantity }, csrf);

    private static async Task<JsonElement> GetCartDataAsync(HttpClient client)
    {
        HttpResponseMessage resp = await client.GetAsync("/api/v1/cart");
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Post, path, body, csrf);

    private static Task<HttpResponseMessage> PutJsonAsync(HttpClient client, string path, object body, string csrf) =>
        SendJsonAsync(client, HttpMethod.Put, path, body, csrf);

    private static Task<HttpResponseMessage> DeleteAsync(HttpClient client, string path, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, path);
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

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
