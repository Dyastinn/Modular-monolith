using Microsoft.EntityFrameworkCore.Migrations;

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

            migrationBuilder.CreateTable(
                name: "stock_item",
                schema: "inventory",
                columns: table => new
                {
                    item_id = table.Column<long>(type: "bigint", nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_item", x => x.item_id);
                    table.CheckConstraint("ck_stock_item_not_over", "reserved <= on_hand");
                    table.CheckConstraint("ck_stock_item_on_hand", "on_hand >= 0");
                    table.CheckConstraint("ck_stock_item_reserved", "reserved >= 0");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stock_item",
                schema: "inventory");
        }
    }
}
