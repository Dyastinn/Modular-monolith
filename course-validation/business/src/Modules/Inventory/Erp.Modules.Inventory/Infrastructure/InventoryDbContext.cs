using System.Data.Common;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Erp.Modules.Inventory.Infrastructure;

internal sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public const string Schema = "inventory";

    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Shipment> Shipments => Set<Shipment>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    // Ids that an event carries must exist before SaveChanges writes the event, so they come from a sequence.
    public Task<long> NextItemIdAsync(CancellationToken ct) => NextAsync($"SELECT nextval('inventory.item_id_seq') AS \"Value\"", ct);
    public Task<long> NextShipmentIdAsync(CancellationToken ct) => NextAsync($"SELECT nextval('inventory.shipment_id_seq') AS \"Value\"", ct);

    // ToList, not Single(): EF would wrap Single() in a subquery.
    private async Task<long> NextAsync(FormattableString sql, CancellationToken ct) =>
        (await Database.SqlQuery<long>(sql).ToListAsync(ct)).Single();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<ItemId>().HaveConversion<ItemIdConverter>();
        c.Properties<ShipmentId>().HaveConversion<ShipmentIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<decimal>().HavePrecision(18, 3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("item_id_seq");
        b.HasSequence<long>("shipment_id_seq");

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<Item>(e =>
        {
            e.ToTable("item", t =>
            {
                t.HasCheckConstraint("ck_item_on_hand", "on_hand >= 0");
                t.HasCheckConstraint("ck_item_reserved", "reserved >= 0");
                // Overselling is impossible at the data level, whatever the code does.
                t.HasCheckConstraint("ck_item_not_over", "reserved <= on_hand");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.Sku).IsUnique();
            e.Property(x => x.Sku).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Unit).HasMaxLength(10);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.Available);
            e.Ignore(x => x.DomainEvents);
        });

        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movement");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Reference).HasMaxLength(50);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => new { x.ItemId, x.OccurredUtc });
            e.HasOne<Item>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Shipment>(e =>
        {
            e.ToTable("shipment");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.OrderNumber).HasMaxLength(30);
            e.HasIndex(x => x.SalesOrderId).IsUnique();   // an order ships once
            e.Ignore(x => x.DomainEvents);
            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("shipment_line", t => t.HasCheckConstraint("ck_shipment_line_qty", "quantity > 0"));
                l.WithOwner().HasForeignKey("ShipmentId");
                l.Property<long>("Id");
                l.HasKey("Id");
            });
            e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }
}

internal sealed class ItemIdConverter() : ValueConverter<ItemId, long>(v => v.Value, v => new ItemId(v));
internal sealed class ShipmentIdConverter() : ValueConverter<ShipmentId, long>(v => v.Value, v => new ShipmentId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new InventoryDbContext(o.Options);
    }
}
