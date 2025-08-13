using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SyncModel_MoneyOwnedAndTotal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ShippedAt",
                table: "orders",
                newName: "shipped_at");

            migrationBuilder.RenameColumn(
                name: "DeliveredAt",
                table: "orders",
                newName: "delivered_at");

            migrationBuilder.RenameColumn(
                name: "CancelReason",
                table: "orders",
                newName: "cancel_reason");

            migrationBuilder.AddColumn<decimal>(
                name: "total_amount",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_orders_created_at",
                table: "orders",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id",
                table: "orders",
                column: "CustomerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_created_at",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_customer_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "total_amount",
                table: "orders");

            migrationBuilder.RenameColumn(
                name: "shipped_at",
                table: "orders",
                newName: "ShippedAt");

            migrationBuilder.RenameColumn(
                name: "delivered_at",
                table: "orders",
                newName: "DeliveredAt");

            migrationBuilder.RenameColumn(
                name: "cancel_reason",
                table: "orders",
                newName: "CancelReason");
        }
    }
}
