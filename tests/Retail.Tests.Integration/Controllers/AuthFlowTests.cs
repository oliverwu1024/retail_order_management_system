using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Retail.Tests.Integration.Controllers;

/// <summary>
/// End-to-end auth tests through the full middleware pipeline (CORS, CSRF, JwtBearer
/// cookie extraction, MVC) — Task 1.1.7's "register → login → call protected
/// endpoint", plus the CSRF and refresh-reuse guarantees from ADR-0007.
/// </summary>
/// <remarks>
/// The factory is shared across the class (one in-memory DB); each test uses a
/// fresh <c>HttpClient</c> (its own cookie jar) and a unique email, so they don't
/// interfere. <c>CreateClient()</c> auto-persists cookies between requests on the
/// same client, mirroring a browser.
/// </remarks>
[Collection("api")]
public class AuthFlowTests
{
    private readonly ApiFactory _factory;

    public AuthFlowTests(ApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_ThenMe_ReturnsAuthenticatedCustomer()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = await GetCsrfAsync(client);
        string email = UniqueEmail();

        HttpResponseMessage register = await PostJsonAsync(
            client, "/api/v1/auth/register",
            new { email, password = "Sup3rSecret!pw", displayName = "Tester" }, csrf);

        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        // Access token must be issued as an HttpOnly cookie (never in the body).
        Assert.Contains(
            register.Headers.GetValues("Set-Cookie"),
            c => c.StartsWith("access_token=", StringComparison.Ordinal)
                 && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        JsonElement body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal(email, body.GetProperty("data").GetProperty("email").GetString());
        Assert.Contains("Customer", Roles(body));
    }

    [Fact]
    public async Task Me_WithoutCookies_Returns401()
    {
        HttpClient client = _factory.CreateClient();

        HttpResponseMessage me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Login_WithSeededAdmin_ReturnsAdministratorRole()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = await GetCsrfAsync(client);

        HttpResponseMessage login = await PostJsonAsync(
            client, "/api/v1/auth/login",
            new { email = "admin@test.local", password = "TestAdmin123456" }, csrf);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        JsonElement body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Administrator", Roles(body));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = await GetCsrfAsync(client);

        HttpResponseMessage login = await PostJsonAsync(
            client, "/api/v1/auth/login",
            new { email = "admin@test.local", password = "definitely-wrong-99" }, csrf);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task StateChangingRequest_WithoutCsrfHeader_Returns403()
    {
        HttpClient client = _factory.CreateClient();
        await GetCsrfAsync(client); // sets the csrf cookie, but we omit the header on purpose

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new { email = "x@test.local", password = "whatever12345" }),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReplayOfRotatedToken_IsRejected()
    {
        HttpClient client = _factory.CreateClient();
        string csrf = await GetCsrfAsync(client);
        string email = UniqueEmail();

        HttpResponseMessage register = await PostJsonAsync(
            client, "/api/v1/auth/register",
            new { email, password = "Sup3rSecret!pw", displayName = "Tester" }, csrf);
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        string firstRefresh = ExtractCookieValue(register, "refresh_token");
        string csrfAfterRegister = ExtractCookieValue(register, "csrf");

        // Rotate the first refresh token away (client now holds its successor).
        HttpResponseMessage rotate = await PostJsonAsync(
            client, "/api/v1/auth/refresh", new { }, csrfAfterRegister);
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);

        // Replay the now-rotated token on a cookieless client → reuse → 401.
        HttpClient bare = _factory.CreateDefaultClient();
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", $"refresh_token={firstRefresh}; csrf={csrfAfterRegister}");
        replay.Headers.Add("X-CSRF-Token", csrfAfterRegister);

        HttpResponseMessage replayResponse = await bare.SendAsync(replay);

        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string UniqueEmail() => $"user-{Guid.NewGuid():N}@test.local";

    private static IReadOnlyList<string?> Roles(JsonElement body) =>
        body.GetProperty("data").GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToList();

    private static async Task<string> GetCsrfAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/auth/csrf");
        response.EnsureSuccessStatusCode();
        return ExtractCookieValue(response, "csrf");
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, object body, string csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf);
        return client.SendAsync(request);
    }

    // Pulls a cookie value out of the response's Set-Cookie headers (the cookie
    // value never contains ';', so splitting on the first ';' is safe).
    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        Assert.True(
            response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies),
            $"Expected a Set-Cookie header carrying '{cookieName}'.");

        string? setCookie = cookies!.FirstOrDefault(c => c.StartsWith(cookieName + "=", StringComparison.Ordinal));
        Assert.NotNull(setCookie);

        string afterName = setCookie!.Substring(cookieName.Length + 1);
        int semicolon = afterName.IndexOf(';');
        return semicolon >= 0 ? afterName.Substring(0, semicolon) : afterName;
    }
}
