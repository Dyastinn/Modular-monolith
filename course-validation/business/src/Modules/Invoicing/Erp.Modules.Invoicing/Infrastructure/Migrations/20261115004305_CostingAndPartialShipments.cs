using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Invoicing.Infrastructure.Migrations
{
    // Each invoice line keeps its item and cost of goods, for margins; an invoice bills one shipment, which may be part of an order.
    public partial class CostingAndPartialShipments : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(CostingAndPartialShipments).Assembly, file);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "item_id",
                schema: "invoicing",
                table: "invoice_line",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "unit_cost",
                schema: "invoicing",
                table: "invoice_line",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            // Invoices from before costing keep no item or cost: their margin is unknown, and reports say so.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.DropColumn(
                name: "item_id",
                schema: "invoicing",
                table: "invoice_line");

            migrationBuilder.DropColumn(
                name: "unit_cost",
                schema: "invoicing",
                table: "invoice_line");
        }
    }
}
