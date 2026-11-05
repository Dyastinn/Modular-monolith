using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Erp.Modules.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateSequence(
                name: "item_id_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "shipment_id_seq",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "item",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    sku = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    allows_fractions = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    on_hand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item", x => x.id);
                    table.CheckConstraint("ck_item_not_over", "reserved <= on_hand");
                    table.CheckConstraint("ck_item_on_hand", "on_hand >= 0");
                    table.CheckConstraint("ck_item_reserved", "reserved >= 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_message",
                schema: "inventory",
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
                name: "shipment",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    sales_order_id = table.Column<long>(type: "bigint", nullable: false),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    shipped_by = table.Column<long>(type: "bigint", nullable: false),
                    shipped_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_movement",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    occurred_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movement", x => x.id);
                    table.ForeignKey(
                        name: "fk_stock_movement_item_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipment_line",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    shipment_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipment_line", x => x.id);
                    table.CheckConstraint("ck_shipment_line_qty", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_shipment_line_shipment_shipment_id",
                        column: x => x.shipment_id,
                        principalSchema: "inventory",
                        principalTable: "shipment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_item_sku",
                schema: "inventory",
                table: "item",
                column: "sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_message_occurred_utc",
                schema: "inventory",
                table: "outbox_message",
                column: "occurred_utc",
                filter: "processed_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_shipment_number",
                schema: "inventory",
                table: "shipment",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_sales_order_id",
                schema: "inventory",
                table: "shipment",
                column: "sales_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shipment_line_shipment_id",
                schema: "inventory",
                table: "shipment_line",
                column: "shipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_movement_item_id_occurred_utc",
                schema: "inventory",
                table: "stock_movement",
                columns: new[] { "item_id", "occurred_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_message",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "shipment_line",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_movement",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "shipment",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "item",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "item_id_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "shipment_id_seq",
                schema: "inventory");
        }
    }
}
