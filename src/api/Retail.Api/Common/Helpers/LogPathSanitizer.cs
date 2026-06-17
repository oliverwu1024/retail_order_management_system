namespace Retail.Api.Common.Helpers;

/// <summary>
/// Redacts secrets embedded in URL paths before they reach logs. Today the only such secret is the
/// Stripe session id in the guest order-lookup route (<c>/orders/by-session/{id}</c>): that id is an
/// unguessable bearer token (anyone holding it can fetch the guest order), so it must never land in
/// logs in a replayable form — and the success page polls that route, so the misses are frequent.
/// </summary>
public static class LogPathSanitizer
{
    private const string SessionLookupMarker = "/orders/by-session/";

    /// <summary>
    /// Masks the session-id segment of the guest order-lookup path (keeping the route shape for ops),
    /// and returns any other path unchanged. Null/empty in → empty out.
    /// </summary>
    public static string Sanitize(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        int markerIndex = path.IndexOf(SessionLookupMarker, StringComparison.OrdinalIgnoreCase);
        return markerIndex >= 0
            ? string.Concat(path.AsSpan(0, markerIndex + SessionLookupMarker.Length), "***")
            : path;
    }
}
