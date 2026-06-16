namespace Retail.Api.Domain.Entities;

/// <summary>
/// An immutable point-in-time copy of a postal address, captured onto an <see cref="Order"/>
/// at placement and persisted as a JSON column (not a row).
/// </summary>
/// <remarks>
/// WHY A SNAPSHOT, NOT AN FK TO <see cref="Address"/>?
/// An order is a historical record. If we referenced the live <see cref="Address"/> row,
/// editing or deleting that address later would silently rewrite where a past order
/// "shipped to". Copying the values onto the order freezes them. The same reasoning drives
/// the SKU/name/price snapshots on <see cref="OrderLine"/>. Guests have no saved address at
/// all, so a snapshot (filled from the checkout form / Stripe) is the only option for them.
///
/// Stored via an EF JSON ValueConverter + ValueComparer on the Order entity (the same
/// pattern as <see cref="ProductVariant.Options"/>), so this stays a plain value object —
/// no Id, no table, no audit fields.
/// </remarks>
public sealed class OrderAddressSnapshot
{
    /// <summary>Recipient name as entered at checkout. Nullable (guests/members may omit).</summary>
    public string? RecipientName { get; set; }

    /// <summary>Street address line 1.</summary>
    public string Line1 { get; set; } = string.Empty;

    /// <summary>Street address line 2 (apartment, suite, etc.). Nullable.</summary>
    public string? Line2 { get; set; }

    /// <summary>City / locality.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>State / province / region. Nullable.</summary>
    public string? Region { get; set; }

    /// <summary>Postal / ZIP code.</summary>
    public string PostalCode { get; set; } = string.Empty;

    /// <summary>ISO-3166 alpha-2 country code (e.g. "AU").</summary>
    public string Country { get; set; } = string.Empty;
}
