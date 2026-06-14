namespace Retail.Api.Common.Constants;

/// <summary>
/// Wire-level names for the auth cookies and the CSRF header. Centralised so the
/// server (which sets them) and any code that reads them can never drift apart.
/// </summary>
/// <remarks>
/// The cookie/header NAMES are part of the contract with the SPA
/// (see CODING_STANDARDS.md §認證 cookie 約定 — <c>lib/csrf.ts</c> reads the
/// <c>csrf</c> cookie and <c>apiClient</c> sends the <c>X-CSRF-Token</c> header).
/// The cookie ATTRIBUTES (HttpOnly/Secure/SameSite/Path/Expires) are NOT here —
/// they live in one place, <c>Identity/AuthCookies.cs</c>, so the security flags
/// are auditable together.
/// </remarks>
public static class AuthConstants
{
    /// <summary>HttpOnly cookie carrying the access JWT. Read by the JwtBearer cookie hook.</summary>
    public const string AccessTokenCookie = "access_token";

    /// <summary>HttpOnly cookie carrying the opaque refresh token. Read only by <c>/auth/refresh</c> and <c>/auth/logout</c>.</summary>
    public const string RefreshTokenCookie = "refresh_token";

    /// <summary>Non-HttpOnly cookie carrying the signed CSRF token. Readable by the SPA so it can echo the value back.</summary>
    public const string CsrfCookie = "csrf";

    /// <summary>Header the SPA must send on every state-changing request, echoing the <see cref="CsrfCookie"/> value.</summary>
    public const string CsrfHeader = "X-CSRF-Token";
}
