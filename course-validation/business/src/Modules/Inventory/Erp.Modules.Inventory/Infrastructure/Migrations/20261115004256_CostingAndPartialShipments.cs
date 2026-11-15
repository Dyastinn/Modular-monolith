using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Inventory.Infrastructure.Migrations
{
    // Items carry a moving-average cost; every movement and shipment line records what it was worth; an order may ship in parts.
    public partial class CostingAndPartialShipments : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(CostingAndPartialShipments).Assembly, file);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_shipment_sales_order_id",
                schema: "inventory",
                table: "shipment");

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movement",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "inventory",
                table: "shipment_line",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "average_cost",
                schema: "inventory",
                table: "item",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_sales_order_id",
                schema: "inventory",
                table: "shipment",
                column: "sales_order_id");

            // Existing stock has no known cost: it reads ₱0 until its next receipt, or a count that states a cost.
            migrationBuilder.Sql(Sql("inventory.search_items.v2.sql"));
            migrationBuilder.Sql(Sql("inventory.get_item_movements.v2.sql"));
            migrationBuilder.Sql(Sql("inventory.search_shipments.v2.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.stock_valuation();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.search_items(text, boolean, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.get_item_movements(bigint, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.search_shipments(bigint, text, boolean, integer, integer);");
            migrationBuilder.Sql(Sql("inventory.search_items.v1.sql"));
            migrationBuilder.Sql(Sql("inventory.get_item_movements.v1.sql"));
            migrationBuilder.Sql(Sql("inventory.search_shipments.v1.sql"));
            migrationBuilder.DropIndex(
                name: "ix_shipment_sales_order_id",
                schema: "inventory",
                table: "shipment");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "stock_movement");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "inventory",
                table: "shipment_line");

            migrationBuilder.DropColumn(
                name: "average_cost",
                schema: "inventory",
                table: "item");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_sales_order_id",
                schema: "inventory",
                table: "shipment",
                column: "sales_order_id",
                unique: true);
        }
    }
}
