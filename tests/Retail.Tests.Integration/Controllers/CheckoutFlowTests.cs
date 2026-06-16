using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Checkout-session tests on real SQL Server (Story 2.2, 3b). The Stripe gateway is faked
/// (see <c>FakeStripeCheckoutGateway</c>), so these exercise "reserve the cart + return a
/// session URL" without touching Stripe. Order creation off the webhook is covered separately.
/// </summary>
[Collection("api")]
public class CheckoutFlowTests
{
    private readonly ApiFactory _factory;

    public CheckoutFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GuestCheckout_ReturnsSessionUrl_AndReservesStock()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Guid variantId = await CreateSellableVariantAsync(admin, adminCsrf, suffix, stock: 5);

        (HttpClient guest, string csrf) = await GuestClientAsync();
        await AddItemAsync(guest, csrf, variantId, 2);

        HttpResponseMessage resp = await StartCheckoutAsync(guest, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.StartsWith("https://stripe.test/", data.GetProperty("url").GetString());

        // The cart's stock is now held.
        Assert.Equal(2, await ReadReservedAsync(variantId));
    }

    [Fact]
    public async Task Checkout_EmptyCart_Returns409()
    {
        (HttpClient guest, string csrf) = await GuestClientAsync();

        HttpResponseMessage resp = await StartCheckoutAsync(guest, csrf);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Checkout_WhenAnotherCheckoutHoldsTheLastUnit_Returns409()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        Guid variantId = await CreateSellableVariantAsync(admin, adminCsrf, suffix, stock: 1);

        (HttpClient guestA, string csrfA) = await GuestClientAsync();
        await AddItemAsync(guestA, csrfA, variantId, 1);
        Assert.Equal(HttpStatusCode.OK, (await StartCheckoutAsync(guestA, csrfA)).StatusCode); // holds the unit

        (HttpClient guestB, string csrfB) = await GuestClientAsync();
        await AddItemAsync(guestB, csrfB, variantId, 1);
        HttpResponseMessage resp = await StartCheckoutAsync(guestB, csrfB);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode); // INVENTORY_INSUFFICIENT
    }

    [Fact]
    public async Task Checkout_WhenTotalOverflowsInt_Returns409_AndHoldsNoStock()
    {
        (HttpClient admin, string adminCsrf) = await AdminClientAsync();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        // A variant priced near int.MaxValue cents — two of them overflow an int subtotal. The
        // checkout total guard (long math + int.MaxValue ceiling) must reject this BEFORE charging
        // at Stripe, so a cart can't be charged here and then rejected at order creation.
        Guid variantId = await CreateSellableVariantAsync(admin, adminCsrf, suffix, stock: 5, priceCents: 2_000_000_000);

        (HttpClient guest, string csrf) = await GuestClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await AddItemAsync(guest, csrf, variantId, 2)).StatusCode);

        HttpResponseMessage resp = await StartCheckoutAsync(guest, csrf);

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        // The guard runs before the reservation, so no stock was held.
        Assert.Equal(0, await ReadReservedAsync(variantId));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> StartCheckoutAsync(HttpClient client, string csrf) =>
        PostJsonAsync(client, "/api/v1/orders/checkout-session", new { returnBaseUrl = "http://localhost:5173" }, csrf);

    private async Task<int> ReadReservedAsync(Guid variantId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return (await db.InventoryItems.AsNoTracking().FirstAsync(i => i.ProductVariantId == variantId)).Reserved;
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

    private async Task<(HttpClient Client, string Csrf)> GuestClientAsync()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        return (client, csrf);
    }

    private static async Task<Guid> CreateSellableVariantAsync(
        HttpClient admin, string csrf, string suffix, int stock, int priceCents = 1999)
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
        return Guid.Parse((await variantResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> AddItemAsync(HttpClient client, string csrf, Guid variantId, int quantity) =>
        PostJsonAsync(client, "/api/v1/cart/items", new { productVariantId = variantId, quantity }, csrf);

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
