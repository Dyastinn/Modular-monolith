using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Modules.Sales.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sales");

            migrationBuilder.CreateSequence(
                name: "sales_order_number_seq",
                schema: "sales");

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    credit_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    outstanding_balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer", x => x.id);
                    table.CheckConstraint("ck_customer_balance", "outstanding_balance >= 0");
                    table.CheckConstraint("ck_customer_credit_limit", "credit_limit >= 0");
                });

            migrationBuilder.CreateTable(
                name: "item_category",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_quantity_per_order = table.Column<int>(type: "integer", nullable: false),
                    required_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_category", x => x.id);
                    table.CheckConstraint("ck_item_category_max_qty", "max_quantity_per_order > 0");
                });

            migrationBuilder.CreateTable(
                name: "sales_order",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<long>(type: "bigint", nullable: false),
                    created_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order", x => x.id);
                    table.CheckConstraint("ck_sales_order_status", "status BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "fk_sales_order_customer_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "sales",
                        principalTable: "customer",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item", x => x.id);
                    table.CheckConstraint("ck_item_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_item_item_category_category_id",
                        column: x => x.category_id,
                        principalSchema: "sales",
                        principalTable: "item_category",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sales_order_line",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order_line", x => x.id);
                    table.CheckConstraint("ck_sales_order_line_price", "unit_price >= 0");
                    table.CheckConstraint("ck_sales_order_line_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_sales_order_line_sales_order_sales_order_id",
                        column: x => x.sales_order_id,
                        principalSchema: "sales",
                        principalTable: "sales_order",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_category_id",
                schema: "sales",
                table: "item",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_sku",
                schema: "sales",
                table: "item",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_category_name",
                schema: "sales",
                table: "item_category",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_customer_id",
                schema: "sales",
                table: "sales_order",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_order_number",
                schema: "sales",
                table: "sales_order",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_status",
                schema: "sales",
                table: "sales_order",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_line_sales_order_id",
                schema: "sales",
                table: "sales_order_line",
                column: "sales_order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "item",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_order_line",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "item_category",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_order",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "customer",
                schema: "sales");

            migrationBuilder.DropSequence(
                name: "sales_order_number_seq",
                schema: "sales");
        }
    }
}
