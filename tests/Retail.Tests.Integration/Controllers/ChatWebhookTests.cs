using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Retail.Api.Ai;
using Retail.Api.Ai.Contracts;
using Retail.Api.Common.Enums;
using Retail.Api.Data;
using Retail.Api.Domain.Entities;
using Retail.Api.Exceptions;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// Support-chat webhook (Phase 5A): POST /api/v1/chat/webhook, gated [Authorize(Roles = Customer)]
/// + CSRF. Runs against the hermetic StubLlmClient (Ai:Mode defaults to "stub"), whose chat transcript
/// drives a real tool-use round-trip: a customer turn → list_my_recent_orders tool call → final reply,
/// all persisted as a ChatSession + ChatMessages. Anonymous (401), back-office (403), and bad input
/// (422) are rejected.
/// </summary>
[Collection("api")]
public class ChatWebhookTests
{
    private readonly ApiFactory _factory;

    public ChatWebhookTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Webhook_AsCustomer_Returns200_AndPersistsTranscript()
    {
        (HttpClient client, string csrf, Guid profileId) = await RegisterCustomerAsync("Ada");
        string conversationId = Guid.NewGuid().ToString();

        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId, message = "Where are my orders?" }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("reply").GetString()));

        // The session + transcript were persisted, owned by the caller, including a tool-call row.
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        ChatSession? session = await db.ChatSessions.AsNoTracking()
            .Include(s => s.Messages)
            .SingleOrDefaultAsync(s => s.ConversationId == conversationId);

        Assert.NotNull(session);
        Assert.Equal(profileId, session!.CustomerProfileId);
        Assert.Contains(session.Messages, m => m.Role == ChatMessageRole.User);
        Assert.Contains(session.Messages, m => m.Role == ChatMessageRole.Assistant);
        Assert.Contains(session.Messages, m => m.Role == ChatMessageRole.Tool && m.ToolName == "list_my_recent_orders");
    }

    [Fact]
    public async Task Webhook_ReusesSession_OnSameConversationId()
    {
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Bo");
        string conversationId = Guid.NewGuid().ToString();

        await PostJsonAsync(client, "/api/v1/chat/webhook", new { conversationId, message = "Hi" }, csrf);
        await PostJsonAsync(client, "/api/v1/chat/webhook", new { conversationId, message = "And again" }, csrf);

        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        int sessions = await db.ChatSessions.CountAsync(s => s.ConversationId == conversationId);
        int userTurns = await db.ChatMessages.CountAsync(m =>
            m.ChatSession.ConversationId == conversationId && m.Role == ChatMessageRole.User);

        Assert.Equal(1, sessions);          // one session, upserted
        Assert.Equal(2, userTurns);         // both turns persisted under it
    }

    [Fact]
    public async Task Webhook_Anonymous_Returns401()
    {
        HttpClient anon = _factory.CreateClient();
        string csrf = ExtractCookie(await anon.GetAsync("/api/v1/auth/csrf"), "csrf");

        HttpResponseMessage resp = await PostJsonAsync(anon, "/api/v1/chat/webhook",
            new { conversationId = Guid.NewGuid().ToString(), message = "Hello?" }, csrf);

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_AsStaff_Returns403()
    {
        // The webhook is for customers; a back-office account has no business in the customer chat.
        (HttpClient staff, string csrf) = await LoginAsync("staff@test.local", "TestStaff123456");

        HttpResponseMessage resp = await PostJsonAsync(staff, "/api/v1/chat/webhook",
            new { conversationId = Guid.NewGuid().ToString(), message = "Hello?" }, csrf);

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_EmptyMessage_Returns422()
    {
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Cy");

        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId = Guid.NewGuid().ToString(), message = "" }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_BracedGuidConversationId_Returns200_NotTruncationError()
    {
        // A braced GUID ("{...}", 38 chars) parses (passes validation) but is > char(36); the service
        // normalizes it to the canonical 36-char form rather than 500-ing on a truncated insert.
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Braces");
        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId = $"{{{Guid.NewGuid()}}}", message = "hi" }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_NonGuidConversationId_Returns422()
    {
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Di");

        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId = "not-a-guid", message = "Hello" }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_MessageTooLong_Returns422()
    {
        (HttpClient client, string csrf, _) = await RegisterCustomerAsync("Verbose");

        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId = Guid.NewGuid().ToString(), message = new string('x', 4001) }, csrf);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Webhook_ForeignConversationId_Returns404_AndDoesNotLeak()
    {
        // Alice opens a conversation; Bob then posts to Alice's conversation id.
        (HttpClient alice, string csrfA, Guid profileA) = await RegisterCustomerAsync("Alice");
        string conversationId = Guid.NewGuid().ToString();
        (await PostJsonAsync(alice, "/api/v1/chat/webhook", new { conversationId, message = "hi" }, csrfA))
            .EnsureSuccessStatusCode();

        (HttpClient bob, string csrfB, _) = await RegisterCustomerAsync("Bob");
        HttpResponseMessage resp = await PostJsonAsync(bob, "/api/v1/chat/webhook",
            new { conversationId, message = "whose chat is this?" }, csrfB);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode); // not-owned ≡ not-found

        // Bob neither read nor appended — the session is still Alice's.
        using IServiceScope scope = _factory.Services.CreateScope();
        RetailDbContext db = scope.ServiceProvider.GetRequiredService<RetailDbContext>();
        ChatSession session = await db.ChatSessions.AsNoTracking().SingleAsync(s => s.ConversationId == conversationId);
        Assert.Equal(profileA, session.CustomerProfileId);
    }

    [Fact]
    public async Task Webhook_WhenProviderFails_Returns200WithFriendlyReply()
    {
        // Swap in a throwing ILlmClient for THIS host only (the shared StubLlmClient never throws),
        // proving the 200-not-503 contract through the full controller + ExceptionMiddleware pipeline.
        using WebApplicationFactory<Program> factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ILlmClient>();
                services.AddScoped<ILlmClient, ThrowingLlmClient>();
            }));

        (HttpClient client, string csrf, _) = await RegisterCustomerAsync(factory, "Outage");

        HttpResponseMessage resp = await PostJsonAsync(client, "/api/v1/chat/webhook",
            new { conversationId = Guid.NewGuid().ToString(), message = "hello" }, csrf);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        JsonElement data = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("reply").GetString()));
    }

    // ── helpers ───────────────────────────────────────────────────────────────────

    private Task<(HttpClient Client, string Csrf, Guid ProfileId)> RegisterCustomerAsync(string displayName) =>
        RegisterCustomerAsync(_factory, displayName);

    private static async Task<(HttpClient Client, string Csrf, Guid ProfileId)> RegisterCustomerAsync(
        WebApplicationFactory<Program> factory, string displayName)
    {
        HttpClient client = factory.CreateClient();
        string csrf = ExtractCookie(await client.GetAsync("/api/v1/auth/csrf"), "csrf");
        HttpResponseMessage register = await PostJsonAsync(client, "/api/v1/auth/register",
            new { email = $"cust-{Guid.NewGuid():N}@test.local", password = "Sup3rSecret!pw", displayName }, csrf);
        register.EnsureSuccessStatusCode();
        csrf = ExtractCookie(register, "csrf");

        JsonElement profile = (await (await client.GetAsync("/api/v1/profile")).Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data");
        return (client, csrf, Guid.Parse(profile.GetProperty("id").GetString()!));
    }

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

/// <summary>A live-provider stand-in that always fails, to prove the webhook's 200-on-outage contract.</summary>
internal sealed class ThrowingLlmClient : ILlmClient
{
    public Task<LlmCompletion> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        throw new ExternalServiceException("simulated AI outage");
}
