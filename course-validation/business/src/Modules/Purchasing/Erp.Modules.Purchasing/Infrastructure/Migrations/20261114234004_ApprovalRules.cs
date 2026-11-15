using System;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Purchasing.Infrastructure.Migrations
{
    // Purchase orders are submitted first; the approval limit is data (₱0 = an admin approves every one, as before).
    public partial class ApprovalRules : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(ApprovalRules).Assembly, file);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_order_status",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.AddColumn<long>(
                name: "rejected_by",
                schema: "purchasing",
                table: "purchase_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "rejected_utc",
                schema: "purchasing",
                table: "purchase_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejection_reason",
                schema: "purchasing",
                table: "purchase_order",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "submitted_utc",
                schema: "purchasing",
                table: "purchase_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "purchasing_settings",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    approval_threshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchasing_settings", x => x.id);
                    table.CheckConstraint("ck_purchasing_settings_approval_threshold", "approval_threshold >= 0");
                    table.CheckConstraint("ck_purchasing_settings_single_row", "id = 1");
                });

            migrationBuilder.InsertData(
                schema: "purchasing",
                table: "purchasing_settings",
                columns: new[] { "id", "approval_threshold", "updated_by", "updated_utc" },
                values: new object[] { 1, 0m, null, null });

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_order_status",
                schema: "purchasing",
                table: "purchase_order",
                sql: "status BETWEEN 0 AND 7");

            // Drafts stay drafts: their buyer may still be editing them, and submits when ready.
            migrationBuilder.Sql(Sql("purchasing.search_purchase_orders.v2.sql"));
            migrationBuilder.Sql(Sql("purchasing.get_purchasing_settings.v1.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.get_purchasing_settings();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.search_purchase_orders(bigint, integer, text, integer, bigint, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.count_purchase_orders(bigint, integer, text, integer, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS purchasing.get_purchase_order(bigint);");
            migrationBuilder.Sql("UPDATE purchasing.purchase_order SET status = 0 WHERE status = 7;");   // back to a waiting draft
            migrationBuilder.Sql(Sql("purchasing.search_purchase_orders.v1.sql"));

            migrationBuilder.DropTable(
                name: "purchasing_settings",
                schema: "purchasing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_purchase_order_status",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "rejected_by",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "rejected_utc",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "rejection_reason",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.DropColumn(
                name: "submitted_utc",
                schema: "purchasing",
                table: "purchase_order");

            migrationBuilder.AddCheckConstraint(
                name: "ck_purchase_order_status",
                schema: "purchasing",
                table: "purchase_order",
                sql: "status BETWEEN 0 AND 6");
        }
    }
}
