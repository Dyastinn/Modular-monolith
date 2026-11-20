using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Yasda.Modules.Tickets.Infrastructure.Migrations
{
    // The read-side SQL functions ship with the schema. Never point a released migration at a changed file.
    public partial class AddReadFunctions : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(AddReadFunctions).Assembly, file);

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql("tickets.priority_rank.v1.sql"));
            migrationBuilder.Sql(Sql("tickets.search_tickets.v1.sql"));
            migrationBuilder.Sql(Sql("tickets.get_ticket.v1.sql"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.get_ticket_messages(bigint, boolean);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.get_ticket(bigint, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.count_tickets(bigint, text, text, bigint, boolean, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.search_tickets(bigint, text, text, bigint, boolean, text, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS tickets.priority_rank(text);");
        }
    }
}
