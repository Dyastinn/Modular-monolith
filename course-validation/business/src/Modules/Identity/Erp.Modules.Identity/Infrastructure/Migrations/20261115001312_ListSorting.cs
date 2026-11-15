using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Infrastructure.Migrations
{
    // Lists sort on the server, by the column a person clicks. Only the read functions change.
    public partial class ListSorting : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(ListSorting).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("identity.search_users.v2.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_users(text, text, boolean, integer, integer);");
            migrationBuilder.Sql(Sql("identity.search_users.v1.sql"));
        }
    }
}
