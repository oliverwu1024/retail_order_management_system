using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Customer product-review tests on real SQL Server (Story 4.1): a verified purchaser can submit
/// once and the review + aggregate appear on the public listing; non-purchasers (422), duplicates
/// (409), anonymous callers (401), and bad ratings (422) are all rejected. Products + paid orders
/// are seeded via the DbContext for a focused setup.
/// </summary>
[Collection("api")]
public class ReviewFlowTests
{
    private const int PriceCents = 2500;
    private readonly ApiFactory _factory;

    public ReviewFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Submit_AsVerifiedPurchaser_Returns201_AndShowsInList()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync("Ada");
        Guid productId = await SeedPurchasedProductAsync(profileId);

        HttpResponseMessage resp = await PostJsonAsync(client, $"/api/v1/products/{productId}/reviews",
            new { rating = 5, body = "Fantastic — exactly as described." }, csrf);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(5, data.GetProperty("rating").GetInt32());
        Assert.Equal("Ada", data.GetProperty("customerName").GetString());

        // The public (anonymous) listing shows the review + the whole-product aggregate.
        HttpClient anon = _factory.CreateClient();
        JsonElement list = (await (await anon.GetAsync($"/api/v1/products/{productId}/reviews"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.Equal(1, list.GetProperty("page").GetProperty("totalCount").GetInt32());

        JsonElement summary = list.GetProperty("summary");
        Assert.Equal(1, summary.GetProperty("count").GetInt32());
        Assert.Equal(5.0, summary.GetProperty("average").GetDouble());
        Assert.Equal(1, summary.GetProperty("distribution")[4].GetInt32()); // the 5-star bucket
    }

    [Fact]
    public async Task Submit_WithoutPurchase_Returns422()
    {
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Bo");
        (Guid productId, _, _) = await SeedProductAsync(); // exists, but this customer never bought it

        HttpResponseMessage resp = await PostJsonAsync(client, $"/api/v1/products/{productId}/reviews",
            new { rating = 4, body = "Tried a friend's one." }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Submit_Twice_Returns409()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync("Cy");
        Guid productId = await SeedPurchasedProductAsync(profileId);

        Assert.Equal(HttpStatusCode.Created, (await PostJsonAsync(client,
            $"/api/v1/products/{productId}/reviews", new { rating = 5, body = "First review." }, csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostJsonAsync(client,
            $"/api/v1/products/{productId}/reviews", new { rating = 3, body = "Changed my mind." }, csrf)).StatusCode);
    }

    [Fact]
    public async Task Submit_Anonymous_Returns401()
    {
        (Guid productId, _, _) = await SeedProductAsync();
        HttpClient anon = _factory.CreateClient();
        string csrf = ExtractCookie(await anon.GetAsync("/api/v1/auth/csrf"), "csrf");

        HttpResponseMessage resp = await PostJsonAsync(anon, $"/api/v1/products/{productId}/reviews",
            new { rating = 5, body = "No account here." }, csrf);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Submit_RatingOutOfRange_Returns422()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync("Di");
        Guid productId = await SeedPurchasedProductAsync(profileId);

        HttpResponseMessage resp = await PostJsonAsync(client, $"/api/v1/products/{productId}/reviews",
            new { rating = 6, body = "Six stars!" }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task List_ProductWithNoReviews_ReturnsZeroedSummary()
    {
        (Guid productId, _, _) = await SeedProductAsync();
        HttpClient anon = _factory.CreateClient();

        JsonElement data = (await (await anon.GetAsync($"/api/v1/products/{productId}/reviews"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        Assert.Equal(0, data.GetProperty("page").GetProperty("totalCount").GetInt32());
        Assert.Equal(0, data.GetProperty("summary").GetProperty("count").GetInt32());
        Assert.Equal(0.0, data.GetProperty("summary").GetProperty("average").GetDouble());
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, string Csrf, Guid ProfileId)> RegisterCustomerAsync(string displayName)
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName }, csrf);
        register.EnsureSuccessStatusCode();
        csrf = ExtractCookie(register, "csrf");

        JsonElement profile = (await (await client.GetAsync("/api/v1/profile")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        return (client, csrf, Guid.Parse(profile.GetProperty("id").GetString()!));
    }

    /// <summary>Seeds a published product (category + variant + stock) and returns its ids/name.</summary>
    private async Task<(Guid ProductId, Guid VariantId, string ProductName)> SeedProductAsync()
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
        var inventory = new InventoryItem { Variant = variant, OnHand = 5 };

        db.AddRange(category, product, variant, inventory);
        await db.SaveChangesAsync();
        return (product.Id, variant.Id, product.Name);
    }

    /// <summary>Seeds a product AND a Paid order for it owned by the customer, so the review passes the purchase gate.</summary>
    private async Task<Guid> SeedPurchasedProductAsync(Guid profileId)
    {
        (Guid productId, Guid variantId, string productName) = await SeedProductAsync();

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var address = new OrderAddressSnapshot { Line1 = "1 Test St", City = "Sydney", PostalCode = "2000", Country = "AU" };
        var order = new Order
        {
            CustomerProfileId = profileId,
            Status = OrderStatus.Paid,
            SubtotalCents = PriceCents,
            TaxCents = 0,
            ShippingCents = 0,
            TotalCents = PriceCents,
            ShippingAddress = address,
            BillingAddress = address,
            PlacedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
        };
        order.Lines.Add(new OrderLine
        {
            ProductVariantId = variantId,
            Quantity = 1,
            UnitPriceCents = PriceCents,
            LineTotalCents = PriceCents,
            SkuSnapshot = $"VAR-{productId:N}"[..8],
            NameSnapshot = productName,
        });

        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return productId;
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
