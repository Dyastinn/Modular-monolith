using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("sales.get_sales_order.v1.sql"));
            migrationBuilder.Sql(Sql("sales.get_sales_order_lines.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v1.sql"));
            migrationBuilder.Sql(Sql("sales.count_sales_orders.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_sales_orders(bigint, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_sales_orders(bigint, integer, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint);");
        }
    }
}
