using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    security_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    password_expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_user", x => x.id);
                    table.CheckConstraint("ck_app_user_role", "role IN ('SalesRep', 'SalesManager', 'Admin')");
                });

            migrationBuilder.CreateTable(
                name: "refresh_token",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    token_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    security_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    absolute_expires_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_token", x => x.id);
                    table.CheckConstraint("ck_refresh_token_expiry", "absolute_expires_utc >= expires_utc");
                    table.ForeignKey(
                        name: "fk_refresh_token_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_user_username",
                schema: "identity",
                table: "app_user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_family_id",
                schema: "identity",
                table: "refresh_token",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_token_hash",
                schema: "identity",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_user_id",
                schema: "identity",
                table: "refresh_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "identity");
        }
    }
}
