using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    // Ships the read-side SQL functions with the schema, so every database that has the tables has the queries.
    // The scripts live as .sql files (embedded in this assembly) so they read and diff like SQL, not C# strings.
    // A released script is never edited: a changed function gets a .v2 file and a NEW migration that points at it.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("sales.get_sales_order.v1.sql"));
            migrationBuilder.Sql(Sql("sales.get_sales_order_lines.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_sales_orders(bigint, integer, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint);");
        }
    }
}
