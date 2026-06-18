using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Admin sentiment dashboard (Phase 4, Story 4.3): GET /api/v1/analytics/sentiment-summary and
/// /products-needing-attention, gated by the new Sentiment.View policy (StoreManager + Administrator,
/// Staff EXCLUDED). Verifies aggregation, the &lt; −0.2 attention filter, and the RBAC matrix.
/// </summary>
[Collection("api")]
public class SentimentDashboardTests
{
    private readonly ApiFactory _factory;

    public SentimentDashboardTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SentimentSummary_AsManager_AggregatesScoredReviews()
    {
        Guid productId = await SeedScoredProductAsync(0.8m, SentimentLabel.Positive);
        (HttpClient manager, _) = await ManagerClientAsync();

        JsonElement data = (await (await manager.GetAsync("/api/v1/analytics/sentiment-summary"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        Assert.True(data.GetProperty("scoredReviews").GetInt32() >= 1);
        bool hasProduct = data.GetProperty("products").EnumerateArray()
            .Any(product => product.GetProperty("productId").GetString() == productId.ToString());
        Assert.True(hasProduct);
    }

    [Fact]
    public async Task ProductsNeedingAttention_FiltersBelowThreshold()
    {
        Guid sad = await SeedScoredProductAsync(-0.6m, SentimentLabel.Negative);
        Guid happy = await SeedScoredProductAsync(0.7m, SentimentLabel.Positive);
        (HttpClient manager, _) = await ManagerClientAsync();

        JsonElement data = (await (await manager.GetAsync("/api/v1/analytics/products-needing-attention"))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");

        List<string?> ids = data.EnumerateArray()
            .Select(product => product.GetProperty("productId").GetString())
            .ToList();
        Assert.Contains(sad.ToString(), ids);
        Assert.DoesNotContain(happy.ToString(), ids);
    }

    [Fact]
    public async Task SentimentSummary_AsStaff_Returns403()
    {
        (HttpClient staff, _) = await StaffClientAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/analytics/sentiment-summary")).StatusCode);
    }

    [Fact]
    public async Task SentimentSummary_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/analytics/sentiment-summary")).StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<Guid> SeedScoredProductAsync(decimal score, SentimentLabel label)
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();

        var user = new ApplicationUser
        {
            UserName = $"u-{suffix}@test.local",
            NormalizedUserName = $"U-{suffix}@TEST.LOCAL",
            Email = $"u-{suffix}@test.local",
            NormalizedEmail = $"U-{suffix}@TEST.LOCAL",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        var profile = new CustomerProfile { AppUserId = user.Id, DisplayName = $"User {suffix}" };
        var category = new Category { Name = $"Cat {suffix}", Slug = $"cat-{suffix}" };
        var product = new Product
        {
            Category = category,
            Sku = $"SKU-{suffix}",
            Slug = $"product-{suffix}",
            Name = $"Product {suffix}",
            IsPublished = true,
        };
        var review = new Review
        {
            Product = product,
            CustomerProfile = profile,
            Rating = label == SentimentLabel.Negative ? (byte)1 : (byte)5,
            Body = "Seeded scored review.",
            SentimentScore = score,
            SentimentLabel = label,
            ProcessedAt = _factory.Services.GetRequiredService<TimeProvider>().GetUtcNow(),
        };

        db.AddRange(user, profile, category, product);
        db.Reviews.Add(review);
        await db.SaveChangesAsync();
        return product.Id;
    }

    private Task<(HttpClient Client, string Csrf)> ManagerClientAsync() =>
        LoginAsync("manager@test.local", "TestManager123456");

    private Task<(HttpClient Client, string Csrf)> StaffClientAsync() =>
        LoginAsync("staff@test.local", "TestStaff123456");

    private async Task<(HttpClient Client, string Csrf)> LoginAsync(string email, string password)
    {
        HttpClient client = _factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email, password }),
        };
        request.Headers.Add("X-CSRF-Token", csrf);
        HttpResponseMessage login = await client.SendAsync(request);
        login.EnsureSuccessStatusCode();
        return (client, ExtractCookie(login, "csrf"));
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
