using System;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    // The approval limit becomes data (company default + per-customer), and over-credit orders wait for accounting.
    public partial class ApprovalRules : Migration
    {
        private static string Sql(string file) => EmbeddedSql.Read(typeof(ApprovalRules).Assembly, file);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "credit_released_by",
                schema: "sales",
                table: "sales_order",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "credit_released_utc",
                schema: "sales",
                table: "sales_order",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "pending_approvals",
                schema: "sales",
                table: "sales_order",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "approval_threshold",
                schema: "sales",
                table: "customer",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sales_settings",
                schema: "sales",
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
                    table.PrimaryKey("pk_sales_settings", x => x.id);
                    table.CheckConstraint("ck_sales_settings_approval_threshold", "approval_threshold >= 0");
                    table.CheckConstraint("ck_sales_settings_single_row", "id = 1");
                });

            migrationBuilder.InsertData(
                schema: "sales",
                table: "sales_settings",
                columns: new[] { "id", "approval_threshold", "updated_by", "updated_utc" },
                values: new object[] { 1, 50000m, null, null });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_pending_approvals",
                schema: "sales",
                table: "sales_order",
                column: "pending_approvals",
                filter: "status = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customer_approval_threshold",
                schema: "sales",
                table: "customer",
                sql: "approval_threshold IS NULL OR approval_threshold >= 0");

            // Orders already waiting were waiting for a manager: the only approval there was.
            migrationBuilder.Sql("UPDATE sales.sales_order SET pending_approvals = 1 WHERE status = 1;");

            migrationBuilder.Sql(Sql("sales.get_sales_order.v2.sql"));
            migrationBuilder.Sql(Sql("sales.search_customers.v2.sql"));
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v2.sql"));
            migrationBuilder.Sql(Sql("sales.get_sales_settings.v1.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_settings();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_sales_orders(bigint, integer, bigint, text, integer, bigint, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.count_sales_orders(bigint, integer, bigint, text, integer, bigint);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.search_customers(bigint, text, text, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS sales.get_sales_order(bigint, bigint);");
            migrationBuilder.Sql(Sql("sales.get_sales_order.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_customers.v1.sql"));
            migrationBuilder.Sql(Sql("sales.search_sales_orders.v1.sql"));

            migrationBuilder.DropTable(
                name: "sales_settings",
                schema: "sales");

            migrationBuilder.DropIndex(
                name: "ix_sales_order_pending_approvals",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customer_approval_threshold",
                schema: "sales",
                table: "customer");

            migrationBuilder.DropColumn(
                name: "credit_released_by",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "credit_released_utc",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "pending_approvals",
                schema: "sales",
                table: "sales_order");

            migrationBuilder.DropColumn(
                name: "approval_threshold",
                schema: "sales",
                table: "customer");
        }
    }
}
