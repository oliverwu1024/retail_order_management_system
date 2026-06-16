using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Retail.Api.Domain.Entities;

namespace Retail.Api.Data;

/// <summary>
/// The EF Core DbContext for the Retail OMS database — the bridge between
/// C# entities and SQL Server.
/// </summary>
/// <remarks>
/// <para>
/// Inherits from <see cref="IdentityDbContext{TUser}"/> (parametrized on
/// <see cref="ApplicationUser"/>) so the seven ASP.NET Identity tables are
/// auto-registered without us having to write a single configuration line:
/// AspNetUsers, AspNetRoles, AspNetUserRoles, AspNetUserClaims,
/// AspNetUserLogins, AspNetUserTokens, AspNetRoleClaims.
/// </para>
/// <para>
/// We pick <c>IdentityDbContext&lt;ApplicationUser&gt;</c> (not the bare
/// <see cref="DbContext"/>) precisely because we want those tables — rolling
/// our own user/role/claim schema is a classic OWASP A07 failure source.
/// We accept the small lock-in to Identity's table naming in exchange for
/// the framework's secure-by-default identity stack.
/// </para>
/// <para>
/// Domain entities (Product, Order, Cart, Voucher, etc.) are added as their
/// phases land — each as a <see cref="DbSet{TEntity}"/> property here plus a
/// dedicated <c>IEntityTypeConfiguration&lt;T&gt;</c> file in
/// <c>Data/Configurations/</c>. We use the configuration-class pattern instead
/// of inline Fluent API in <c>OnModelCreating</c> because at ~30+ entities the
/// inline approach becomes unreadable.
/// </para>
/// </remarks>
public class RetailDbContext : IdentityDbContext<ApplicationUser>
{
    /// <summary>
    /// Standard EF Core constructor — receives <see cref="DbContextOptions{TContext}"/>
    /// from DI (registered in <c>Program.cs</c> via <c>AddDbContext&lt;RetailDbContext&gt;</c>).
    /// </summary>
    /// <remarks>
    /// The generic type parameter on <see cref="DbContextOptions{TContext}"/>
    /// matters: it's how DI distinguishes options for this DbContext from
    /// options for any other DbContext registered in the same container.
    /// If we ever add a second DbContext (e.g. a read-only reporting context),
    /// each gets its own typed options.
    /// </remarks>
    public RetailDbContext(DbContextOptions<RetailDbContext> options)
        : base(options)
    {
    }

    // Note: we do NOT declare `public DbSet<ApplicationUser> Users { get; set; }`
    // because IdentityDbContext<TUser> already exposes `Users` (and `Roles`,
    // `UserRoles`, etc.). Declaring it again would shadow the base property
    // and confuse the schema generator. Future domain DbSets DO get declared
    // explicitly — they're not provided by the base.

    /// <summary>
    /// Issued refresh tokens (stored as hashes). Backs ADR-0007's refresh-token
    /// rotation + reuse detection; mapped by <c>RefreshTokenConfiguration</c>.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // ── Catalog (Story 1.2) ──────────────────────────────────────────────────
    /// <summary>Product categories (self-referencing tree). Soft-deletable.</summary>
    public DbSet<Category> Categories => Set<Category>();

    /// <summary>Catalogue products. Soft-deletable.</summary>
    public DbSet<Product> Products => Set<Product>();

    /// <summary>Purchasable product variants (price + options + stock).</summary>
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();

    /// <summary>Product gallery images (general + variant-specific).</summary>
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    /// <summary>Per-variant stock (1:1 with <see cref="ProductVariant"/>).</summary>
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    // ── Customer profile (Story 1.4) ─────────────────────────────────────────
    /// <summary>Customer domain profiles (1:1 with an Identity user). Not soft-deletable.</summary>
    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();

    /// <summary>Saved shipping/billing addresses (owned by a <see cref="CustomerProfile"/>). Not soft-deletable.</summary>
    public DbSet<Address> Addresses => Set<Address>();

    // ── Cart & Orders (Phase 2) ──────────────────────────────────────────────
    /// <summary>Shopping carts — member or anonymous-guest. Not soft-deletable (status-tombstoned).</summary>
    public DbSet<Cart> Carts => Set<Cart>();

    /// <summary>Cart line items.</summary>
    public DbSet<CartItem> CartItems => Set<CartItem>();

    /// <summary>Soft stock holds placed between checkout-start and payment.</summary>
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();

    /// <summary>Placed orders (member or guest).</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>Order line items (with SKU/name/price snapshots).</summary>
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    /// <summary>Payment events (Stripe charges / refunds).</summary>
    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Stripe webhook idempotency ledger (technical append-only table).</summary>
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();

    /// <summary>Per-order price breakdown (1:1 with <see cref="Order"/>).</summary>
    public DbSet<OrderPriceBreakdown> OrderPriceBreakdowns => Set<OrderPriceBreakdown>();

    /// <summary>
    /// EF Core's hook for schema configuration via the Fluent API. Called
    /// once at model-build time (effectively at startup), then cached.
    /// </summary>
    /// <param name="builder">The model builder EF uses to construct the schema.</param>
    /// <remarks>
    /// <para>
    /// <c>base.OnModelCreating(builder)</c> MUST come first. <see cref="IdentityDbContext{TUser}"/>'s
    /// override is where the seven Identity tables get configured (table names,
    /// composite keys on join tables, indexes on normalized email/username,
    /// etc.). Skipping the base call would leave Identity half-configured and
    /// the migration would silently omit critical indexes.
    /// </para>
    /// <para>
    /// <c>ApplyConfigurationsFromAssembly</c> scans the assembly containing
    /// this DbContext for any class implementing <c>IEntityTypeConfiguration&lt;T&gt;</c>
    /// and applies it. This is the convention we use for every future entity:
    /// each gets its own configuration class in <c>Data/Configurations/</c>
    /// (e.g. <c>ProductConfiguration : IEntityTypeConfiguration&lt;Product&gt;</c>).
    /// Result: <c>OnModelCreating</c> stays a one-liner forever, regardless of
    /// how many entities we add.
    /// </para>
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // 1. Let IdentityDbContext register its seven Identity tables.
        //    Without this call, EF will not produce the AspNet* tables and
        //    the migration will be silently incomplete.
        base.OnModelCreating(builder);

        // 2. Apply every IEntityTypeConfiguration<T> found in this assembly
        //    (RefreshToken, ApplicationUser, Category/Product/ProductVariant/
        //    InventoryItem, ...). New entities just drop a configuration class into
        //    Data/Configurations/ and are picked up automatically.
        builder.ApplyConfigurationsFromAssembly(typeof(RetailDbContext).Assembly);

        // 3. Soft-delete global query filters (Task 1.2.9). Rows with IsDeleted=true
        //    become invisible to every ordinary query; the admin "show deleted" view
        //    opts back in with IgnoreQueryFilters(). Only Product + Category are
        //    soft-deletable (DATABASE_DESIGN §1).
        builder.Entity<Product>().HasQueryFilter(p => !p.IsDeleted);
        builder.Entity<Category>().HasQueryFilter(c => !c.IsDeleted);

        // 4. Order-number sequence (Phase 2). Seq_OrderNumber yields human-facing order
        //    numbers starting at 10001; Order.OrderNumber defaults to NEXT VALUE FOR it
        //    (see OrderConfiguration). Declared here, not in a configuration class, because
        //    a sequence is a model-level object, not an entity-type mapping.
        builder.HasSequence<int>("Seq_OrderNumber").StartsAt(10001).IncrementsBy(1);
    }
}
