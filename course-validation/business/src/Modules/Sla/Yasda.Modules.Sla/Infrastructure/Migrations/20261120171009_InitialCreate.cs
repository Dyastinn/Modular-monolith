using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Yasda.Modules.Sla.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sla");

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "sla",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sla_clock",
                schema: "sla",
                columns: table => new
                {
                    ticket_id = table.Column<long>(type: "bigint", nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    respond_minutes = table.Column<int>(type: "integer", nullable: false),
                    resolve_minutes = table.Column<int>(type: "integer", nullable: false),
                    response_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    responded_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolve_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolved_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    last_reopened_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    priority_changed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sla_clock", x => x.ticket_id);
                    table.CheckConstraint("ck_sla_clock_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
                });

            migrationBuilder.CreateTable(
                name: "sla_policy",
                schema: "sla",
                columns: table => new
                {
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    respond_minutes = table.Column<int>(type: "integer", nullable: false),
                    resolve_minutes = table.Column<int>(type: "integer", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sla_policy", x => x.priority);
                    table.CheckConstraint("ck_sla_policy_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
                    table.CheckConstraint("ck_sla_policy_targets", "respond_minutes > 0 AND resolve_minutes >= respond_minutes");
                });

            migrationBuilder.InsertData(
                schema: "sla",
                table: "sla_policy",
                columns: new[] { "priority", "resolve_minutes", "respond_minutes", "updated_by_id", "updated_by_name", "updated_utc" },
                values: new object[,]
                {
                    { "Critical", 240, 60, 0L, "Installation default", new DateTime(2026, 11, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "High", 1440, 240, 0L, "Installation default", new DateTime(2026, 11, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "Low", 7200, 1440, 0L, "Installation default", new DateTime(2026, 11, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { "Normal", 4320, 480, 0L, "Installation default", new DateTime(2026, 11, 20, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "sla",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "sla");

            migrationBuilder.DropTable(
                name: "sla_clock",
                schema: "sla");

            migrationBuilder.DropTable(
                name: "sla_policy",
                schema: "sla");
        }
    }
}
