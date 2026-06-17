namespace Retail.Api.Common.Constants;

/// <summary>
/// Canonical role names — the single source of truth for the four RBAC roles.
/// </summary>
/// <remarks>
/// <para>
/// These constants are referenced in three places that MUST agree: the seeder
/// (<c>IdentityDataSeeder</c>) that creates the roles, the <c>[Authorize(Roles = ...)]</c>
/// attributes on controllers, and the role assignment at registration. Using
/// string literals in those places instead would risk a silent typo
/// ("Adminstrator") that compiles fine and then fails authorization at runtime.
/// </para>
/// <para>
/// The role hierarchy (widest to narrowest authority) is
/// <see cref="Administrator"/> ⊃ <see cref="StoreManager"/> ⊃ <see cref="Staff"/>,
/// with <see cref="Customer"/> as the storefront-only role. The full permission
/// matrix lands in Phase 3 (REQUIREMENTS §1.3); Phase 1 only needs the names to
/// exist and be seeded.
/// </para>
/// </remarks>
public static class Roles
{
    /// <summary>Storefront shopper. Assigned automatically on self-signup.</summary>
    public const string Customer = "Customer";

    /// <summary>Fulfilment / inventory operator. Narrowest admin role.</summary>
    public const string Staff = "Staff";

    /// <summary>Everything Staff can do, plus user management, refunds, and reports.</summary>
    public const string StoreManager = "StoreManager";

    /// <summary>Full authority. The seeded default admin holds this role.</summary>
    public const string Administrator = "Administrator";

    /// <summary>
    /// All four role names — iterated by the seeder to ensure each exists.
    /// Order is widest-storefront-first; it has no authorization meaning.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        Customer,
        Staff,
        StoreManager,
        Administrator,
    };

    /// <summary>
    /// Named authorization policy keys — the single source of truth for the Phase 3
    /// capability matrix (see <c>PHASE_3_SCOPE.md §3.1</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The matrix is capability-shaped and overlapping (refund = StoreManager+Administrator,
    /// fulfil = Staff+StoreManager+Administrator, manage-catalog = Administrator-only), so
    /// instead of scattering <c>[Authorize(Roles = "StoreManager,Administrator")]</c> strings
    /// across every controller, each capability is a <em>named policy</em> defined ONCE in
    /// <c>Program.cs</c> (an <c>AddAuthorization</c> block, each policy a <c>RequireRole(...)</c>)
    /// and applied as <c>[Authorize(Policy = Roles.Policies.X)]</c>. A rule change is one edit.
    /// </para>
    /// <para>
    /// <c>AuditExport</c>/<c>ReportsExport</c> are defined but unused in the MVP — export is
    /// deferred, which is exactly what makes the "Staff is read-only on audit/reports" tier real
    /// once an export button exists. The storefront <c>[Authorize(Roles = Customer)]</c>
    /// attributes stay role-based; only the admin matrix is policy-based.
    /// </para>
    /// </remarks>
    public static class Policies
    {
        /// <summary>List all orders + view any order's detail. Staff + StoreManager + Administrator.</summary>
        public const string OrdersView = "Orders.View";

        /// <summary>Mark shipped / mark delivered. Staff + StoreManager + Administrator.</summary>
        public const string OrdersFulfill = "Orders.Fulfill";

        /// <summary>Admin-initiated refund. StoreManager + Administrator.</summary>
        public const string OrdersRefund = "Orders.Refund";

        /// <summary>Adjust stock on hand. Staff + StoreManager + Administrator.</summary>
        public const string InventoryAdjust = "Inventory.Adjust";

        /// <summary>Search the audit log (view-only in the MVP). Staff + StoreManager + Administrator.</summary>
        public const string AuditView = "Audit.View";

        /// <summary>View reports (sales-by-day). Staff + StoreManager + Administrator.</summary>
        public const string ReportsView = "Reports.View";

        /// <summary>Create/list Staff accounts. StoreManager + Administrator.</summary>
        public const string UsersManageStaff = "Users.ManageStaff";

        /// <summary>Create StoreManager accounts. Administrator only.</summary>
        public const string UsersManageManagers = "Users.ManageManagers";

        /// <summary>All catalog writes (products/variants/images). Administrator only.</summary>
        public const string CatalogManage = "Catalog.Manage";

        /// <summary>Export the audit log. StoreManager + Administrator. (Defined; export deferred.)</summary>
        public const string AuditExport = "Audit.Export";

        /// <summary>Export reports. StoreManager + Administrator. (Defined; export deferred.)</summary>
        public const string ReportsExport = "Reports.Export";
    }
}
