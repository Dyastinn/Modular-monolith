using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Identity.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("identity.search_users.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.count_sign_in_attempts(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_sign_in_attempts(text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.user_directory();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.count_users(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_users(text, integer, integer);");
        }
    }
}
