using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Migrations
{
    // Lists sort on the server, by the column a person clicks. Only the read functions change.
    public partial class ListSorting : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(ListSorting).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("purchasing.search_suppliers.v2.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.search_suppliers(bigint, text, boolean, text, boolean, integer, integer);");
            migrationBuilder.Sql(Sql("purchasing.search_suppliers.v1.sql"));
        }
    }
}
