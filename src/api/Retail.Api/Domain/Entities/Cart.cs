using Retail.Api.Common.Enums;
using Retail.Api.Domain.Common;

namespace Retail.Api.Domain.Entities;

/// <summary>
/// A shopping cart (DATABASE_DESIGN §3.8). Owned EITHER by a logged-in customer
/// (<see cref="CustomerProfileId"/>) OR by an anonymous guest (<see cref="AnonymousKey"/>) —
/// exactly one, never both. On login a guest's cart is merged into their member cart
/// (see docs/PHASE_2_SCOPE.md §3.2).
/// </summary>
/// <remarks>
/// NOT soft-deletable: a cart's terminal state is a <see cref="CartStatus"/>
/// (<c>Converted</c> after checkout, <c>Abandoned</c> after expiry) rather than an
/// <c>IsDeleted</c> flag — only Product/Category/Review use soft delete (DATABASE_DESIGN §1).
/// </remarks>
public class Cart : IAuditableEntity
{
    /// <summary>Surrogate PK.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning customer profile; <c>null</c> for an anonymous (guest) cart.</summary>
    public Guid? CustomerProfileId { get; set; }

    /// <summary>Navigation to the owning profile (null for guest carts).</summary>
    public CustomerProfile? CustomerProfile { get; set; }

    /// <summary>
    /// Opaque guest key, mirrored in the <c>X-Anon-Cart-Key</c> cookie; <c>null</c> once the
    /// cart belongs to a profile. Stored as <c>char(36)</c> — a GUID in string form.
    /// </summary>
    public string? AnonymousKey { get; set; }

    /// <summary>Lifecycle status. New carts start <see cref="CartStatus.Open"/>.</summary>
    public CartStatus Status { get; set; } = CartStatus.Open;

    /// <summary>
    /// Sliding 30-minute expiry, refreshed on each mutation. Past this the
    /// <c>CartExpirySweeper</c> abandons the cart and releases its reservations.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The lines in this cart.</summary>
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();

    // ── IAuditableEntity (stamped by AuditingInterceptor) ────────────────────
    /// <inheritdoc />
    public DateTimeOffset CreatedAt { get; set; }
    /// <inheritdoc />
    public string? CreatedBy { get; set; }
    /// <inheritdoc />
    public DateTimeOffset? UpdatedAt { get; set; }
    /// <inheritdoc />
    public string? UpdatedBy { get; set; }
}
