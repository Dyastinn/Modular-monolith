using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("purchasing.search_purchase_orders.v1.sql"));
            migrationBuilder.Sql(Sql("purchasing.search_suppliers.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.count_suppliers(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.search_suppliers(bigint, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.get_purchase_order_lines(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.get_purchase_order(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.count_purchase_orders(bigint, integer, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.search_purchase_orders(bigint, integer, text, integer, integer);");
        }
    }
}
