using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Inventory.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("inventory.get_item_movements.v1.sql"));
            migrationBuilder.Sql(Sql("inventory.search_items.v1.sql"));
            migrationBuilder.Sql(Sql("inventory.search_shipments.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.count_shipments(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.search_shipments(bigint, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.count_items(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.search_items(text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.count_item_movements(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS inventory.get_item_movements(bigint, integer, integer);");
        }
    }
}
