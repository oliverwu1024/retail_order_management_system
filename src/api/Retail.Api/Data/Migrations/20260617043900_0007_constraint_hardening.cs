using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class _0007_constraint_hardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cart_AnonymousKey_Status",
                table: "Cart");

            migrationBuilder.DropIndex(
                name: "IX_Cart_CustomerProfileId_Status",
                table: "Cart");

            migrationBuilder.AddCheckConstraint(
                name: "CK_InventoryReservation_Owner",
                table: "InventoryReservation",
                sql: "([CartId] IS NOT NULL AND [OrderId] IS NULL) OR ([CartId] IS NULL AND [OrderId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "UX_Cart_OpenPerAnonymousKey",
                table: "Cart",
                column: "AnonymousKey",
                unique: true,
                filter: "[Status] = 1 AND [AnonymousKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Cart_OpenPerProfile",
                table: "Cart",
                column: "CustomerProfileId",
                unique: true,
                filter: "[Status] = 1 AND [CustomerProfileId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_InventoryReservation_Owner",
                table: "InventoryReservation");

            migrationBuilder.DropIndex(
                name: "UX_Cart_OpenPerAnonymousKey",
                table: "Cart");

            migrationBuilder.DropIndex(
                name: "UX_Cart_OpenPerProfile",
                table: "Cart");

            migrationBuilder.CreateIndex(
                name: "IX_Cart_AnonymousKey_Status",
                table: "Cart",
                columns: new[] { "AnonymousKey", "Status" },
                filter: "[AnonymousKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cart_CustomerProfileId_Status",
                table: "Cart",
                columns: new[] { "CustomerProfileId", "Status" });
        }
    }
}
