using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yasda.Modules.Identity.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file)
        {
            return EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);
        }

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("identity.search_users.v1.sql"));
            migrationBuilder.Sql(Sql("identity.get_user.v1.sql"));
            migrationBuilder.Sql(Sql("identity.list_staff.v1.sql"));
            migrationBuilder.Sql(Sql("identity.search_teams.v1.sql"));
            migrationBuilder.Sql(Sql("identity.get_team.v1.sql"));
            migrationBuilder.Sql(Sql("identity.teams_of_user.v1.sql"));
            migrationBuilder.Sql(Sql("identity.notify_list.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.notify_list(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.teams_of_user(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.get_team_members(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.get_team(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.count_teams(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_teams(text, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.list_staff(bigint, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.get_user(bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.count_sign_in_attempts(text, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_sign_in_attempts(text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.count_users(text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS identity.search_users(text, text, boolean, integer, integer);");
        }
    }
}
