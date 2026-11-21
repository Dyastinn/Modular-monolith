using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Yasda.Modules.Tickets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tickets");

            migrationBuilder.CreateSequence(
                name: "ticket_id_seq",
                schema: "tickets");

            migrationBuilder.CreateTable(
                name: "automation_settings",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    close_resolved_after_days = table.Column<int>(type: "integer", nullable: true),
                    remind_pending_after_days = table.Column<int>(type: "integer", nullable: true),
                    close_pending_after_days = table.Column<int>(type: "integer", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by_id = table.Column<long>(type: "bigint", nullable: false),
                    updated_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_automation_settings", x => x.id);
                    table.CheckConstraint("ck_automation_settings_days", "(close_resolved_after_days IS NULL OR close_resolved_after_days > 0) AND (remind_pending_after_days IS NULL OR remind_pending_after_days > 0) AND (close_pending_after_days IS NULL OR close_pending_after_days > 0)");
                });

            migrationBuilder.CreateTable(
                name: "category",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    team_id = table.Column<long>(type: "bigint", nullable: true),
                    team_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_category", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "tickets",
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
                name: "ticket",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requester_id = table.Column<long>(type: "bigint", nullable: false),
                    requester_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assignee_id = table.Column<long>(type: "bigint", nullable: true),
                    assignee_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    first_response_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    resolved_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    closed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    pending_since_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reminder_sent_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    category_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    team_id = table.Column<long>(type: "bigint", nullable: true),
                    team_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket", x => x.id);
                    table.CheckConstraint("ck_ticket_priority", "priority IN ('Low', 'Normal', 'High', 'Critical')");
                    table.CheckConstraint("ck_ticket_status", "status IN ('New', 'Open', 'Pending', 'Resolved', 'Closed', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "ticket_sla",
                schema: "tickets",
                columns: table => new
                {
                    ticket_id = table.Column<long>(type: "bigint", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    response_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    response_met = table.Column<bool>(type: "boolean", nullable: true),
                    resolve_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    resolution_met = table.Column<bool>(type: "boolean", nullable: true),
                    next_due_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    at_risk_from_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    paused = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_sla", x => x.ticket_id);
                });

            migrationBuilder.CreateTable(
                name: "ticket_message",
                schema: "tickets",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    author_id = table.Column<long>(type: "bigint", nullable: false),
                    author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ticket_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ticket_message", x => x.id);
                    table.ForeignKey(
                        name: "fk_ticket_message_ticket_ticket_id",
                        column: x => x.ticket_id,
                        principalSchema: "tickets",
                        principalTable: "ticket",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "tickets",
                table: "automation_settings",
                columns: new[] { "id", "close_pending_after_days", "close_resolved_after_days", "remind_pending_after_days", "updated_by_id", "updated_by_name", "updated_utc" },
                values: new object[] { 1, null, null, null, 0L, "Installation default", new DateTime(2026, 11, 21, 0, 0, 0, 0, DateTimeKind.Utc) });

            migrationBuilder.CreateIndex(
                name: "ix_category_name",
                schema: "tickets",
                table: "category",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_category_team_id",
                schema: "tickets",
                table: "category",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "tickets",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_assignee_id",
                schema: "tickets",
                table: "ticket",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_requester_id",
                schema: "tickets",
                table: "ticket",
                column: "requester_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_status_updated_utc",
                schema: "tickets",
                table: "ticket",
                columns: new[] { "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_ticket_team_id",
                schema: "tickets",
                table: "ticket",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_message_ticket_id",
                schema: "tickets",
                table: "ticket_message",
                column: "ticket_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_settings",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "category",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_message",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "ticket_sla",
                schema: "tickets");

            migrationBuilder.DropTable(
                name: "ticket",
                schema: "tickets");

            migrationBuilder.DropSequence(
                name: "ticket_id_seq",
                schema: "tickets");
        }
    }
}
