using Retail.Api.Common.Constants;

namespace Retail.Api.Identity;

/// <summary>
/// The single place every auth cookie's security attributes are set. Keeping the
/// flags here (rather than scattered across controller actions) means the security
/// posture — <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Strict</c>, <c>Path</c> —
/// is auditable in one file (ADR-0007).
/// </summary>
public static class AuthCookies
{
    /// <summary>Writes the access-JWT cookie: HttpOnly so JavaScript/XSS cannot read it.</summary>
    public static void WriteAccessToken(HttpResponse response, string token, DateTimeOffset expiresAt, bool secure) =>
        response.Cookies.Append(
            AuthConstants.AccessTokenCookie,
            token,
            BuildOptions(httpOnly: true, secure, expiresAt));

    /// <summary>Writes the refresh-token cookie: HttpOnly, longer-lived than the access cookie.</summary>
    public static void WriteRefreshToken(HttpResponse response, string token, DateTimeOffset expiresAt, bool secure) =>
        response.Cookies.Append(
            AuthConstants.RefreshTokenCookie,
            token,
            BuildOptions(httpOnly: true, secure, expiresAt));

    /// <summary>
    /// Writes the CSRF cookie: NON-HttpOnly on purpose, so the SPA can read it and
    /// echo it back in the <c>X-CSRF-Token</c> header. Issued as a session cookie
    /// (no Expires) — it lives for the browser session and is re-issued on each auth.
    /// </summary>
    public static void WriteCsrf(HttpResponse response, string token, bool secure) =>
        response.Cookies.Append(
            AuthConstants.CsrfCookie,
            token,
            BuildOptions(httpOnly: false, secure, expiresAt: null));

    /// <summary>
    /// Clears all three auth cookies. Used on logout and on a failed refresh. The
    /// options must match the path/secure/samesite used when setting, or the
    /// browser will not consider it the same cookie and won't remove it.
    /// </summary>
    public static void Clear(HttpResponse response, bool secure)
    {
        var options = new CookieOptions
        {
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        };

        response.Cookies.Delete(AuthConstants.AccessTokenCookie, options);
        response.Cookies.Delete(AuthConstants.RefreshTokenCookie, options);
        response.Cookies.Delete(AuthConstants.CsrfCookie, options);
    }

    private static CookieOptions BuildOptions(bool httpOnly, bool secure, DateTimeOffset? expiresAt) => new()
    {
        HttpOnly = httpOnly,
        Secure = secure,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expiresAt,
        // Marks the cookie as required for the app to function, so it is not
        // suppressed by ASP.NET Core's cookie-consent (GDPR) gate.
        IsEssential = true,
    };
}
