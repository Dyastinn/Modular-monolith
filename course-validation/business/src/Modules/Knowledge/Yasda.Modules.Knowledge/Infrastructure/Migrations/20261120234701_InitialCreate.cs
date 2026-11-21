using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Yasda.Modules.Knowledge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "knowledge");

            migrationBuilder.CreateTable(
                name: "article",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by_id = table.Column<long>(type: "bigint", nullable: false),
                    created_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_edited_by_id = table.Column<long>(type: "bigint", nullable: false),
                    last_edited_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article", x => x.id);
                    table.CheckConstraint("ck_article_status", "status IN ('Draft', 'InReview', 'Published', 'Archived')");
                    table.CheckConstraint("ck_article_visibility", "visibility IN ('Public', 'Internal')");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "knowledge",
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
                name: "article_editor",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    added_by_id = table.Column<long>(type: "bigint", nullable: false),
                    added_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    added_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    article_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_editor", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_editor_article_article_id",
                        column: x => x.article_id,
                        principalSchema: "knowledge",
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_history",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    action = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<long>(type: "bigint", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version_number = table.Column<int>(type: "integer", nullable: true),
                    article_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_history_article_article_id",
                        column: x => x.article_id,
                        principalSchema: "knowledge",
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_version",
                schema: "knowledge",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    visibility = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    published_by_id = table.Column<long>(type: "bigint", nullable: false),
                    published_by_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    published_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    article_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_version", x => x.id);
                    table.ForeignKey(
                        name: "fk_article_version_article_article_id",
                        column: x => x.article_id,
                        principalSchema: "knowledge",
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "article_view_day",
                schema: "knowledge",
                columns: table => new
                {
                    article_id = table.Column<long>(type: "bigint", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    views = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_article_view_day", x => new { x.article_id, x.day });
                    table.ForeignKey(
                        name: "fk_article_view_day_article_article_id",
                        column: x => x.article_id,
                        principalSchema: "knowledge",
                        principalTable: "article",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_article_created_by_id",
                schema: "knowledge",
                table: "article",
                column: "created_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_status_updated_utc",
                schema: "knowledge",
                table: "article",
                columns: new[] { "status", "updated_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_article_editor_article_id_user_id",
                schema: "knowledge",
                table: "article_editor",
                columns: new[] { "article_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_article_history_article_id",
                schema: "knowledge",
                table: "article_history",
                column: "article_id");

            migrationBuilder.CreateIndex(
                name: "ix_article_version_article_id_number",
                schema: "knowledge",
                table: "article_version",
                columns: new[] { "article_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "knowledge",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "article_editor",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "article_history",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "article_version",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "article_view_day",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "knowledge");

            migrationBuilder.DropTable(
                name: "article",
                schema: "knowledge");
        }
    }
}
