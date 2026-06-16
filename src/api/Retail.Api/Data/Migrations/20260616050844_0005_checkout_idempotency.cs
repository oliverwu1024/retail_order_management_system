using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Retail.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class _0005_checkout_idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Payment_StripeSessionId",
                table: "Payment");

            migrationBuilder.CreateIndex(
                name: "UX_Payment_StripeSessionId",
                table: "Payment",
                column: "StripeSessionId",
                unique: true,
                filter: "[StripeSessionId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Order_Identity",
                table: "Order",
                sql: "([CustomerProfileId] IS NOT NULL AND [GuestEmail] IS NULL) OR ([CustomerProfileId] IS NULL AND [GuestEmail] IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Payment_StripeSessionId",
                table: "Payment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Order_Identity",
                table: "Order");

            migrationBuilder.CreateIndex(
                name: "IX_Payment_StripeSessionId",
                table: "Payment",
                column: "StripeSessionId",
                filter: "[StripeSessionId] IS NOT NULL");
        }
    }
}
