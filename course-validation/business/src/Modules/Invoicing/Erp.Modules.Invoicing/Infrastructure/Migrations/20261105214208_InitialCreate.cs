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
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_by = table.Column<long>(type: "bigint", nullable: true),
                    voided_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
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
                    table.CheckConstraint("ck_payment_amount", "amount > 0");
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "invoice",
                schema: "invoicing");

            migrationBuilder.DropSequence(
                name: "payment_id_seq",
                schema: "invoicing");
        }
    }
}
