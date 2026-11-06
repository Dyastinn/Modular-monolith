using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

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
                name: "applied_payment",
                schema: "sales",
                columns: table => new
                {
                    payment_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    applied_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reversed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applied_payment", x => x.payment_id);
                });

            migrationBuilder.CreateTable(
                name: "customer",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    payment_terms_days = table.Column<int>(type: "integer", nullable: false),
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
                    max_quantity_per_order = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    required_role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_category", x => x.id);
                    table.CheckConstraint("ck_item_category_max_qty", "max_quantity_per_order > 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "sales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    processed_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_message", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "special_discount_program",
                schema: "sales",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    id_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vat_exempt = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_special_discount_program", x => x.code);
                    table.CheckConstraint("ck_special_discount_program_rate", "rate > 0 AND rate < 1");
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
                    submitted_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    approved_by = table.Column<long>(type: "bigint", nullable: true),
                    approved_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejected_by = table.Column<long>(type: "bigint", nullable: true),
                    rejected_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    shipped_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    special_discount_program = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_id_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    special_discount_id_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    special_discount_vat_rate_removed = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    special_discount_vat_exemption = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    special_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sales_order", x => x.id);
                    table.CheckConstraint("ck_sales_order_status", "status BETWEEN 0 AND 5");
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
                    id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    allows_fractions = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category_id = table.Column<long>(type: "bigint", nullable: true),
                    is_for_sale = table.Column<bool>(type: "boolean", nullable: false),
                    tax_type = table.Column<int>(type: "integer", nullable: false),
                    is_special_discount_eligible = table.Column<bool>(type: "boolean", nullable: false)
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
                    item_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_type = table.Column<int>(type: "integer", nullable: false),
                    special_discount_eligible = table.Column<bool>(type: "boolean", nullable: false),
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

            migrationBuilder.InsertData(
                schema: "sales",
                table: "special_discount_program",
                columns: new[] { "code", "id_label", "is_active", "name", "rate", "vat_exempt" },
                values: new object[,]
                {
                    { "PWD", "PWD ID No.", true, "Person with disability", 0.20m, true },
                    { "SC", "OSCA ID No.", true, "Senior citizen", 0.20m, true }
                });

            migrationBuilder.CreateIndex(
                name: "ix_customer_name",
                schema: "sales",
                table: "customer",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_item_category_id",
                schema: "sales",
                table: "item",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_category_name",
                schema: "sales",
                table: "item_category",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "sales",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_created_by",
                schema: "sales",
                table: "sales_order",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_customer_id_status",
                schema: "sales",
                table: "sales_order",
                columns: new[] { "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_order_order_number",
                schema: "sales",
                table: "sales_order",
                column: "order_number",
                unique: true);

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
                name: "applied_payment",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "item",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "sales_order_line",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "special_discount_program",
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
