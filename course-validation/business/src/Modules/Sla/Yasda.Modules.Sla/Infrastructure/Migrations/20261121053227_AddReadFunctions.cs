using Microsoft.EntityFrameworkCore.Migrations;
using Yasda.Shared.BuildingBlocks;

#nullable disable

namespace Yasda.Modules.Sla.Infrastructure.Migrations
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
            migrationBuilder.Sql(Sql("sla.get_policies.v1.sql"));
            migrationBuilder.Sql(Sql("sla.get_calendar.v1.sql"));
            migrationBuilder.Sql(Sql("sla.get_calendar_days.v1.sql"));
            migrationBuilder.Sql(Sql("sla.get_calendar_holidays.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sla.get_policies();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sla.get_calendar();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sla.get_calendar_days();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sla.get_calendar_holidays();");
        }
    }
}
