using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;

namespace Retail.Api.Services;

/// <summary>
/// Cart business logic (Story 2.1). Resolves the caller to a member or guest cart, snapshots
/// prices at add-time, dedups lines by variant, and merges a guest cart into the member cart
/// on login. Every method returns a <see cref="CartResult"/> so the controller knows what to
/// do with the anonymous-cart cookie.
/// </summary>
public interface ICartService
{
    Task<CartResult> GetCartAsync(CartCaller caller, CancellationToken ct);
    Task<CartResult> AddItemAsync(CartCaller caller, AddCartItemRequest request, CancellationToken ct);
    Task<CartResult> UpdateItemAsync(CartCaller caller, Guid productVariantId, UpdateCartItemRequest request, CancellationToken ct);
    Task<CartResult> RemoveItemAsync(CartCaller caller, Guid productVariantId, CancellationToken ct);
    Task<CartResult> ClearAsync(CartCaller caller, CancellationToken ct);
}

/// <summary>
/// Who is asking for a cart. Exactly one identity path is taken: authenticated
/// (<paramref name="AppUserId"/> set) → the member's cart; otherwise the guest cart named by
/// <paramref name="AnonymousKey"/> (which is null on a guest's first ever call — the service
/// then mints a new key and returns it for the cookie).
/// </summary>
public sealed record CartCaller(string? AppUserId, string? AnonymousKey);

/// <summary>
/// A cart plus the cookie instruction for the controller. <paramref name="AnonymousKey"/> is
/// the guest key to (re)write as the anon-cart cookie, or <c>null</c> to mean "this is a
/// member cart — delete any anon-cart cookie" (e.g. immediately after a login-time merge).
/// </summary>
public sealed record CartResult(CartDto Cart, string? AnonymousKey);
