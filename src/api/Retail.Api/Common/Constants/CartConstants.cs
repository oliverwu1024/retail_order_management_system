namespace Retail.Api.Common.Constants;

/// <summary>
/// Wire-level names for the cart's anonymous-guest cookie. Kept beside
/// <see cref="AuthConstants"/> so cart and auth cookie names can't collide.
/// </summary>
/// <remarks>
/// The cookie carries an opaque GUID that identifies a guest's cart before they log in.
/// It is HttpOnly (no JS needs it — the cart API resolves it server-side) and, unlike the
/// <c>Strict</c> auth cookies, is written <c>SameSite=Lax</c> so it survives the top-level
/// navigation back from Stripe's hosted checkout. Attributes live in the cart controller's
/// cookie helper, mirroring how <c>AuthCookies</c> centralises the auth flags.
/// </remarks>
public static class CartConstants
{
    /// <summary>HttpOnly cookie holding the guest cart key (a GUID). Absent once the cart belongs to a logged-in customer.</summary>
    public const string AnonymousCartKeyCookie = "anon_cart_key";
}
