using System.Security.Claims;

namespace Retail.Api.Common.Abstractions;

/// <summary>
/// Default <see cref="ICurrentUserAccessor"/> for HTTP requests: reads the
/// Identity user id from the current request's <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONE place that knows the audit pipeline's "current user" comes
/// from <c>HttpContext</c>. Registered Scoped in <c>Program.cs</c> because it
/// wraps the request-scoped <see cref="IHttpContextAccessor"/>.
/// </para>
/// <para>
/// We read <see cref="ClaimTypes.NameIdentifier"/> — the claim ASP.NET Identity
/// stores the user id in, and the same value <c>UserManager.GetUserId()</c>
/// reads — so we get the id without taking a dependency on <c>UserManager</c>
/// (which would add a needless DB round-trip on every save).
/// </para>
/// </remarks>
public sealed class HttpContextCurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? UserId
    {
        get
        {
            // HttpContext is null when there is no active request (background
            // services, migration runners, tests). Treat that as anonymous.
            var user = _httpContextAccessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            return user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }
    }
}
