using Microsoft.Extensions.Logging;
using Retail.Api.Common.Enums;
using Retail.Api.Domain.Entities;
using Retail.Api.DTOs.Requests;
using Retail.Api.DTOs.Responses;
using Retail.Api.Exceptions;
using Retail.Api.Mappers;
using Retail.Api.Repositories;

namespace Retail.Api.Services;

/// <summary>
/// Cart business logic (Story 2.1).
/// </summary>
/// <remarks>
/// <para>
/// OWNERSHIP: a cart belongs to a logged-in customer (by <c>CustomerProfileId</c>) OR a
/// guest (by an anonymous key carried in a cookie). <see cref="ResolveAsync"/> turns the
/// <see cref="CartCaller"/> into the right tracked cart and tells the controller what to do
/// with the anon cookie.
/// </para>
/// <para>
/// MERGE-ON-LOGIN is lazy: whenever an authenticated caller still presents a guest cookie,
/// the guest cart's lines are folded into the member cart and the guest cart is abandoned —
/// so it happens on the first cart touch after login regardless of how they authenticated.
/// </para>
/// <para>
/// PRICE SNAPSHOT: a line records the variant's price at add-time
/// (<c>UnitPriceCentsSnapshot</c>) so the cart total is stable mid-session; checkout
/// re-validates against the live price in a later chunk. Re-adding a variant bumps the
/// existing line (the unique index forbids duplicate lines), capped at <see cref="MaxLineQuantity"/>.
/// </para>
/// </remarks>
public sealed class CartService : ICartService
{
    // 30-minute sliding lifetime (DATABASE_DESIGN §3.8), refreshed on every mutation.
    private static readonly TimeSpan CartLifetime = TimeSpan.FromMinutes(30);

    // Per-line ceiling — mirrors the request validators; also clamps merged quantities.
    private const int MaxLineQuantity = 99;

    private readonly ICartRepository _repo;
    private readonly ICustomerProfileService _profiles; // resolves (lazy-creates) the member's profile id
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CartService> _logger;

    public CartService(
        ICartRepository repo,
        ICustomerProfileService profiles,
        TimeProvider timeProvider,
        ILogger<CartService> logger)
    {
        _repo = repo;
        _profiles = profiles;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CartResult> GetCartAsync(CartCaller caller, CancellationToken ct)
    {
        (Cart? cart, string? cookieKey) = await ResolveAsync(caller, create: false, ct);
        // A member GET can create + merge a cart from a lingering guest cookie — persist that.
        // No-op (no DB round-trip) when nothing changed.
        await _repo.SaveChangesAsync(ct);
        return Build(cart, cookieKey);
    }

    /// <inheritdoc />
    public async Task<CartResult> AddItemAsync(CartCaller caller, AddCartItemRequest request, CancellationToken ct)
    {
        (Cart? cart, string? cookieKey) = await ResolveAsync(caller, create: true, ct);
        Cart target = cart!; // create:true guarantees a cart

        ProductVariant variant = await _repo.GetSellableVariantAsync(request.ProductVariantId, ct)
            ?? throw new NotFoundException($"Variant '{request.ProductVariantId}' is not available for purchase.");

        CartItem? line = target.Items.FirstOrDefault(i => i.ProductVariantId == variant.Id);
        if (line is null)
        {
            target.Items.Add(new CartItem
            {
                CartId = target.Id,
                ProductVariantId = variant.Id,
                Quantity = request.Quantity,
                UnitPriceCentsSnapshot = variant.PriceCents, // snapshot the live price now
            });
        }
        else
        {
            line.Quantity = Math.Min(line.Quantity + request.Quantity, MaxLineQuantity);
        }

        TouchExpiry(target);
        await _repo.SaveChangesAsync(ct);
        _logger.LogInformation("Cart {CartId}: added variant {VariantId} x{Quantity}.", target.Id, variant.Id, request.Quantity);
        return Build(target, cookieKey);
    }

    /// <inheritdoc />
    public async Task<CartResult> UpdateItemAsync(CartCaller caller, Guid productVariantId, UpdateCartItemRequest request, CancellationToken ct)
    {
        (Cart? cart, string? cookieKey) = await ResolveAsync(caller, create: false, ct);

        // 404 if the caller has no cart or the variant isn't a line — we never reveal more.
        CartItem line = cart?.Items.FirstOrDefault(i => i.ProductVariantId == productVariantId)
            ?? throw new NotFoundException($"Variant '{productVariantId}' is not in the cart.");

        line.Quantity = request.Quantity;
        TouchExpiry(cart!);
        await _repo.SaveChangesAsync(ct);
        return Build(cart, cookieKey);
    }

    /// <inheritdoc />
    public async Task<CartResult> RemoveItemAsync(CartCaller caller, Guid productVariantId, CancellationToken ct)
    {
        (Cart? cart, string? cookieKey) = await ResolveAsync(caller, create: false, ct);

        CartItem? line = cart?.Items.FirstOrDefault(i => i.ProductVariantId == productVariantId);
        if (cart is not null && line is not null)
        {
            cart.Items.Remove(line); // required relationship → EF deletes the orphaned row
            TouchExpiry(cart);
            await _repo.SaveChangesAsync(ct);
        }

        // Removing a line that isn't there is idempotent success, not a 404.
        return Build(cart, cookieKey);
    }

    /// <inheritdoc />
    public async Task<CartResult> ClearAsync(CartCaller caller, CancellationToken ct)
    {
        (Cart? cart, string? cookieKey) = await ResolveAsync(caller, create: false, ct);

        if (cart is not null && cart.Items.Count > 0)
        {
            cart.Items.Clear(); // required relationship → EF deletes all orphaned rows
            TouchExpiry(cart);
            await _repo.SaveChangesAsync(ct);
        }

        return Build(cart, cookieKey);
    }

    // ── ownership resolution ────────────────────────────────────────────────────

    // Returns the caller's cart (null = none yet) and the key the anon cookie should hold
    // (null = member or no guest cart → controller clears the cookie). With create=true a
    // cart is guaranteed; with create=false a member cart is only materialised when there's
    // a guest cart to merge into it (so a bare GET doesn't spawn empty carts for every visit).
    private async Task<(Cart? Cart, string? CookieKey)> ResolveAsync(CartCaller caller, bool create, CancellationToken ct)
    {
        if (caller.AppUserId is { Length: > 0 } appUserId)
        {
            Cart? memberCart = await ResolveMemberAsync(appUserId, caller.AnonymousKey, create, ct);
            return (memberCart, null); // members never carry an anon cookie afterwards
        }

        return await ResolveGuestAsync(caller.AnonymousKey, create, ct);
    }

    private async Task<Cart?> ResolveMemberAsync(string appUserId, string? anonymousKey, bool create, CancellationToken ct)
    {
        Guid profileId = (await _profiles.GetMyProfileAsync(appUserId, ct)).Id; // lazy-creates the profile

        Cart? memberCart = await _repo.GetOpenCartByProfileAsync(profileId, ct);
        Cart? guestCart = anonymousKey is { Length: > 0 }
            ? await _repo.GetOpenCartByAnonymousKeyAsync(anonymousKey, ct)
            : null;

        bool hasGuestItems = guestCart is { Items.Count: > 0 };
        if (memberCart is null && (create || hasGuestItems))
        {
            memberCart = await CreateCartAsync(customerProfileId: profileId, anonymousKey: null, ct);
        }

        if (memberCart is not null && guestCart is not null && guestCart.Id != memberCart.Id)
        {
            MergeInto(memberCart, guestCart);
        }

        return memberCart;
    }

    private async Task<(Cart? Cart, string? CookieKey)> ResolveGuestAsync(string? anonymousKey, bool create, CancellationToken ct)
    {
        Cart? cart = anonymousKey is { Length: > 0 }
            ? await _repo.GetOpenCartByAnonymousKeyAsync(anonymousKey, ct)
            : null;

        if (cart is null && create)
        {
            cart = await CreateCartAsync(customerProfileId: null, anonymousKey: Guid.NewGuid().ToString(), ct);
        }

        return (cart, cart?.AnonymousKey);
    }

    // Folds a guest cart's lines into the member cart (summing duplicate variants, capped),
    // then abandons the guest cart. The caller's single SaveChanges commits it atomically.
    private void MergeInto(Cart memberCart, Cart guestCart)
    {
        foreach (CartItem guestItem in guestCart.Items)
        {
            CartItem? line = memberCart.Items.FirstOrDefault(i => i.ProductVariantId == guestItem.ProductVariantId);
            if (line is null)
            {
                memberCart.Items.Add(new CartItem
                {
                    CartId = memberCart.Id,
                    ProductVariantId = guestItem.ProductVariantId,
                    Quantity = Math.Min(guestItem.Quantity, MaxLineQuantity),
                    UnitPriceCentsSnapshot = guestItem.UnitPriceCentsSnapshot,
                });
            }
            else
            {
                line.Quantity = Math.Min(line.Quantity + guestItem.Quantity, MaxLineQuantity);
            }
        }

        guestCart.Status = CartStatus.Abandoned; // merged away — won't resurface as an open cart
        TouchExpiry(memberCart);
        _logger.LogInformation("Merged guest cart {GuestCartId} into member cart {MemberCartId}.", guestCart.Id, memberCart.Id);
    }

    private async Task<Cart> CreateCartAsync(Guid? customerProfileId, string? anonymousKey, CancellationToken ct)
    {
        var cart = new Cart
        {
            CustomerProfileId = customerProfileId,
            AnonymousKey = anonymousKey,
            Status = CartStatus.Open,
            ExpiresAt = _timeProvider.GetUtcNow().Add(CartLifetime),
        };
        await _repo.AddCartAsync(cart, ct);
        return cart;
    }

    private void TouchExpiry(Cart cart) =>
        cart.ExpiresAt = _timeProvider.GetUtcNow().Add(CartLifetime);

    private static CartResult Build(Cart? cart, string? cookieKey) =>
        new(cart?.ToDto() ?? EmptyCart(), cookieKey);

    // Returned when the caller has no cart yet (e.g. a guest's first GET) — a well-formed
    // empty cart rather than a 404, so the storefront just renders "your cart is empty".
    private static CartDto EmptyCart() =>
        new(Guid.Empty, Array.Empty<CartItemDto>(), SubtotalCents: 0, TotalQuantity: 0, ExpiresAt: default);
}
