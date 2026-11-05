using Erp.Modules.Identity.Domain;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Erp.Modules.Identity.Infrastructure;

internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // How every Identity DbContext is configured — at runtime AND by `dotnet ef` — so the two never drift.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, System.Data.Common.DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        b.Entity<AppUser>(e =>
        {
            e.ToTable("app_user", t => t.HasCheckConstraint("ck_app_user_role",
                $"role IN ('{Roles.SalesRep}', '{Roles.SalesManager}', '{Roles.Admin}')"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(500);
            e.Property(x => x.Role).HasMaxLength(50);
            // PostgreSQL bumps the hidden xmin column on every update — a free row version for lost-update checks.
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_token", t => t.HasCheckConstraint("ck_refresh_token_expiry", "absolute_expires_utc >= expires_utc"));
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.FamilyId);
            e.Property(x => x.TokenHash).HasMaxLength(200);
            e.Property(x => x.ReplacedByHash).HasMaxLength(200);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);   // same schema: an FK is fine
            // Two refreshes racing with the SAME token: the UPDATE checks revoked_utc is still NULL, so only one
            // rotation can win; the loser gets a concurrency exception and is treated as token reuse.
            e.Property(x => x.RevokedUtc).IsConcurrencyToken();
            e.Ignore(x => x.IsRevoked);
        });
    }
}

// Lets `dotnet ef migrations add` build the context without starting the API. No database is touched.
internal sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<IdentityDbContext>();
        IdentityDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=erp_design_time"));
        return new IdentityDbContext(o.Options);
    }
}
