using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Retail.Api.Data;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Admin support-chat diagnostics (Phase 5A, Chunk 3): GET /api/v1/chat/sessions(/{id}), gated by the
/// new Chat.View policy = StoreManager + Administrator (Staff and Customer excluded, mirroring
/// Sentiment.View). Sessions are seeded by a customer's webhook turn (hermetic stub).
/// </summary>
[Collection("api")]
public class ChatDiagnosticsTests
{
    private readonly ApiFactory _factory;

    public ChatDiagnosticsTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListSessions_AsManager_IncludesASeededSession()
    {
        string conversationId = await SeedSessionAsync();
        (HttpClient manager, _) = await LoginAsync("manager@test.local", "TestManager123456");

        JsonElement data = await GetJsonAsync(manager, "/api/v1/chat/sessions?Page=1&PageSize=100");

        Assert.Contains(
            data.GetProperty("items").EnumerateArray(),
            s => s.GetProperty("conversationId").GetString() == conversationId);
    }

    [Fact]
    public async Task GetSession_AsManager_ReturnsMessageHistory()
    {
        string conversationId = await SeedSessionAsync();
        Guid id = await SessionIdAsync(conversationId);
        (HttpClient manager, _) = await LoginAsync("manager@test.local", "TestManager123456");

        JsonElement data = await GetJsonAsync(manager, $"/api/v1/chat/sessions/{id}");

        Assert.Equal(conversationId, data.GetProperty("conversationId").GetString());

        // Chronological order within the turn: User → Tool(list_my_recent_orders) → Assistant.
        List<string?> roles = data.GetProperty("messages").EnumerateArray()
            .Select(m => m.GetProperty("role").GetString()).ToList();
        Assert.True(roles.Count >= 2);
        Assert.Equal("User", roles[0]);
        int tool = roles.IndexOf("Tool");
        int assistant = roles.IndexOf("Assistant");
        Assert.True(tool >= 0 && tool < assistant, "the tool call should precede the assistant reply");
    }

    [Fact]
    public async Task GetSession_AsStaff_Returns403()
    {
        (HttpClient staff, _) = await LoginAsync("staff@test.local", "TestStaff123456");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/chat/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task GetSession_AsCustomer_Returns403()
    {
        (HttpClient customer, _) = await RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync($"/api/v1/chat/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task ListSessions_AsAdmin_Returns200()
    {
        (HttpClient admin, _) = await LoginAsync("admin@test.local", "TestAdmin123456");
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/chat/sessions")).StatusCode);
    }

    [Fact]
    public async Task ListSessions_AsStaff_Returns403()
    {
        // Chat.View excludes Staff (SM + Admin only) — same tier as the sentiment dashboard.
        (HttpClient staff, _) = await LoginAsync("staff@test.local", "TestStaff123456");
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/chat/sessions")).StatusCode);
    }

    [Fact]
    public async Task ListSessions_AsCustomer_Returns403()
    {
        (HttpClient customer, _) = await RegisterCustomerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/v1/chat/sessions")).StatusCode);
    }

    [Fact]
    public async Task GetSession_Unknown_Returns404()
    {
        (HttpClient admin, _) = await LoginAsync("admin@test.local", "TestAdmin123456");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/chat/sessions/{Guid.NewGuid()}")).StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private async Task<string> SeedSessionAsync()
    {
        (HttpClient customer, string csrf) = await RegisterCustomerAsync();
        string conversationId = Guid.NewGuid().ToString();
        (await PostJsonAsync(customer, "/api/v1/chat/webhook", new { conversationId, message = "hi" }, csrf))
            .EnsureSuccessStatusCode();
        return conversationId;
    }

    private async Task<Guid> SessionIdAsync(string conversationId)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        return await db.ChatSessions.Where(s => s.ConversationId == conversationId).Select(s => s.Id).SingleAsync();
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
