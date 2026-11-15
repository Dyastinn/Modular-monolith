using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    // Each order line records how much has shipped; an order may be partly shipped, and closed short.
    public partial class CostingAndPartialShipments : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(CostingAndPartialShipments).Assembly, file);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_order_status",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.AddColumn<decimal>(
                name: "shipped_quantity",
                schema: "sales",
                table: "sales_order_line",
                type: "numeric(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_order_line_shipped",
                schema: "sales",
                table: "sales_order_line",
                sql: "shipped_quantity >= 0 AND shipped_quantity <= quantity");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_order_status",
                schema: "sales",
                table: "sales_order",
                sql: "status BETWEEN 0 AND 7");

            // Orders that already shipped shipped in full.
            migrationBuilder.Sql("UPDATE sales.sales_order_line l SET shipped_quantity = l.quantity FROM sales.sales_order o WHERE o.id = l.sales_order_id AND o.status = 5;");
            migrationBuilder.Sql(Sql("sales.get_sales_order_lines.v2.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint, bigint);");
            migrationBuilder.Sql(Sql("sales.get_sales_order.v1.sql"));   // holds the v1 lines function
            migrationBuilder.Sql(Sql("sales.get_sales_order.v2.sql"));   // and puts the order header back to v2
            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_order_line_shipped",
                schema: "sales",
                table: "sales_order_line");

            migrationBuilder.DropCheckConstraint(
                name: "ck_sales_order_status",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "shipped_quantity",
                schema: "sales",
                table: "sales_order_line");

            migrationBuilder.AddCheckConstraint(
                name: "ck_sales_order_status",
                schema: "sales",
                table: "sales_order",
                sql: "status BETWEEN 0 AND 5");
        }
    }
}
