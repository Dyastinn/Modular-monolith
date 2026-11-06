using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Modules.Invoicing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "invoicing");

            migrationBuilder.CreateSequence(
                name: "payment_id_seq",
                schema: "invoicing");

            migrationBuilder.CreateTable(
                name: "company_profile",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    registered_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    registered_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tax_regime = table.Column<int>(type: "integer", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    invoice_title = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    permit_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    series_range = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    permit_date_issued = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    updated_by = table.Column<long>(type: "bigint", nullable: true),
                    updated_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_profile", x => x.id);
                    table.CheckConstraint("ck_company_profile_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "document_counter",
                schema: "invoicing",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_counter", x => x.name);
                });

            migrationBuilder.CreateTable(
                name: "invoice",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    seller_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    seller_trade_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    seller_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    seller_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    seller_invoice_title = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    seller_permit_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_series_range = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    seller_permit_date_issued = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    shipment_id = table.Column<long>(type: "bigint", nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    customer_tin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    customer_address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issue_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_regime = table.Column<int>(type: "integer", nullable: false),
                    vat_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    vatable_sales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    zero_rated_sales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    vat_exempt_sales = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    special_discount_program = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_id_label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_holder_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    special_discount_id_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    special_discount_rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    special_discount_vat_rate_removed = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    special_discount_vat_exemption = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    special_discount_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by = table.Column<long>(type: "bigint", nullable: true),
                    voided_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice", x => x.id);
                    table.CheckConstraint("ck_invoice_paid", "amount_paid >= 0 AND amount_paid <= total");
                    table.CheckConstraint("ck_invoice_status", "status BETWEEN 0 AND 3");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "invoicing",
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
                name: "withholding_certificate",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    customer_id = table.Column<long>(type: "bigint", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    quarter = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_on = table.Column<DateOnly>(type: "date", nullable: false),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false),
                    recorded_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voided_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_withholding_certificate", x => x.id);
                    table.CheckConstraint("ck_withholding_certificate_amount", "amount > 0");
                    table.CheckConstraint("ck_withholding_certificate_quarter", "quarter BETWEEN 1 AND 4");
                });

            migrationBuilder.CreateTable(
                name: "invoice_line",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_type = table.Column<int>(type: "integer", nullable: false),
                    special_discount_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_line", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoice_line_invoice_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment",
                schema: "invoicing",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tax_withheld = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recorded_by = table.Column<long>(type: "bigint", nullable: false),
                    recorded_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    voided_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    invoice_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment", x => x.id);
                    table.CheckConstraint("ck_payment_amount", "amount >= 0 AND tax_withheld >= 0 AND amount + tax_withheld > 0");
                    table.ForeignKey(
                        name: "fk_payment_invoice_invoice_id",
                        column: x => x.invoice_id,
                        principalSchema: "invoicing",
                        principalTable: "invoice",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "invoicing",
                table: "company_profile",
                columns: new[] { "id", "invoice_title", "permit_date_issued", "permit_number", "registered_address", "registered_name", "series_range", "tax_regime", "tin", "trade_name", "updated_by", "updated_utc", "vat_rate" },
                values: new object[] { 1, "SALES INVOICE", null, null, "", "", null, 0, "", null, null, null, 0.12m });

            migrationBuilder.InsertData(
                schema: "invoicing",
                table: "document_counter",
                columns: new[] { "name", "next_value" },
                values: new object[] { "invoice", 1L });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_customer_id_status",
                schema: "invoicing",
                table: "invoice",
                columns: new[] { "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_number",
                schema: "invoicing",
                table: "invoice",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_shipment_id",
                schema: "invoicing",
                table: "invoice",
                column: "shipment_id",
                unique: true,
                filter: "status <> 3");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_line_invoice_id",
                schema: "invoicing",
                table: "invoice_line",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "invoicing",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_invoice_id",
                schema: "invoicing",
                table: "payment",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_withholding_certificate_year_quarter_customer_id",
                schema: "invoicing",
                table: "withholding_certificate",
                columns: new[] { "year", "quarter", "customer_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_profile",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "document_counter",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice_line",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "payment",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "withholding_certificate",
                schema: "invoicing");

            migrationBuilder.DropTable(
                name: "invoice",
                schema: "invoicing");

            migrationBuilder.DropSequence(
                name: "payment_id_seq",
                schema: "invoicing");
        }
    }
}
