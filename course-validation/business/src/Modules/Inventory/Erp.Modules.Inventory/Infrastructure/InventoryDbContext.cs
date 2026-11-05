using Erp.Modules.Inventory.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Modules.Inventory.Infrastructure;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<StockItem> StockItems => Set<StockItem>();

    // How every Inventory DbContext is configured — at runtime AND by `dotnet ef` — so the two never drift.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, System.Data.Common.DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();   // StockItem.OnHand → inventory.stock_item.on_hand

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<StockItem>(e =>
        {
            e.ToTable("stock_item", t =>
            {
                t.HasCheckConstraint("ck_stock_item_on_hand", "on_hand >= 0");
                t.HasCheckConstraint("ck_stock_item_reserved", "reserved >= 0");
                // "Oversold" is impossible at the data level, whatever the code does.
                t.HasCheckConstraint("ck_stock_item_not_over", "reserved <= on_hand");
            });
            e.HasKey(x => x.ItemId);
            e.Property(x => x.ItemId).ValueGeneratedNever();
            e.Ignore(x => x.Available);
        });
    }
}

// Lets `dotnet ef migrations add` build the context without starting the API. No database is touched.
internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new InventoryDbContext(o.Options);
    }
}
