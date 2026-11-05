using System.Data.Common;
using Erp.Modules.Purchasing.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Erp.Modules.Purchasing.Infrastructure;

internal sealed class PurchasingDbContext(DbContextOptions<PurchasingDbContext> options) : DbContext(options)
{
    public const string Schema = "purchasing";

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    public async Task<string> NextOrderNumberAsync(DateTime nowUtc, CancellationToken ct)
    {
        var next = (await Database.SqlQuery<long>($"SELECT nextval('purchasing.purchase_order_number_seq') AS \"Value\"")
            .ToListAsync(ct)).Single();
        return DocumentNumber.Format("PO", next, nowUtc);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<SupplierId>().HaveConversion<SupplierIdConverter>();
        c.Properties<PurchaseOrderId>().HaveConversion<PurchaseOrderIdConverter>();
        c.Properties<ItemId>().HaveConversion<ItemIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        c.Properties<Money>().HaveConversion<MoneyConverter>().HavePrecision(18, 2);
        c.Properties<Quantity>().HaveConversion<QuantityConverter>().HavePrecision(18, 3);
        c.Properties<decimal>().HavePrecision(18, 3);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("purchase_order_number_seq");

        // No events today, but every module DbContext maps its outbox so the dispatcher can poll every schema.
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<Supplier>(e =>
        {
            e.ToTable("supplier");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => x.Name);
            e.Property(x => x.Tin).HasMaxLength(20);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Phone).HasMaxLength(50);
        });

        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("purchase_order", t => t.HasCheckConstraint("ck_purchase_order_status", "status BETWEEN 0 AND 6"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Number).HasMaxLength(30);
            e.HasIndex(x => x.Number).IsUnique();
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasIndex(x => new { x.SupplierId, x.Status });
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.Total);

            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("purchase_order_line", t =>
                {
                    t.HasCheckConstraint("ck_purchase_order_line_qty", "quantity > 0");
                    t.HasCheckConstraint("ck_purchase_order_line_received", "received_quantity >= 0 AND received_quantity <= quantity");
                    t.HasCheckConstraint("ck_purchase_order_line_cost", "unit_cost >= 0");
                });
                l.WithOwner().HasForeignKey("PurchaseOrderId");
                l.HasKey(x => x.Id);
                l.Property(x => x.Id).UseIdentityColumn();
                l.Property(x => x.Sku).HasMaxLength(50);
                l.Property(x => x.ItemName).HasMaxLength(200);
                l.Property(x => x.Unit).HasMaxLength(10);
                l.Ignore(x => x.LineTotal);
                l.Ignore(x => x.Outstanding);
            });
            e.Navigation(x => x.Lines).HasField("_lines").UsePropertyAccessMode(PropertyAccessMode.Field);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        // A receipt changes only line rows; touch the order so its xmin catches two clerks receiving at once.
        if (ChangeTracker.Entries<PurchaseOrderLine>().Any(l => l.State != EntityState.Unchanged))
            foreach (var po in ChangeTracker.Entries<PurchaseOrder>().Where(o => o.State == EntityState.Unchanged))
                po.Property(o => o.Status).IsModified = true;

        return base.SaveChangesAsync(ct);
    }
}

internal sealed class SupplierIdConverter() : ValueConverter<SupplierId, long>(v => v.Value, v => new SupplierId(v));
internal sealed class PurchaseOrderIdConverter() : ValueConverter<PurchaseOrderId, long>(v => v.Value, v => new PurchaseOrderId(v));
internal sealed class ItemIdConverter() : ValueConverter<ItemId, long>(v => v.Value, v => new ItemId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));
internal sealed class MoneyConverter() : ValueConverter<Money, decimal>(v => v.Amount, v => new Money(v));
internal sealed class QuantityConverter() : ValueConverter<Quantity, decimal>(v => v.Value, v => new Quantity(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class PurchasingDbContextFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<PurchasingDbContext>();
        PurchasingDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new PurchasingDbContext(o.Options);
    }
}
