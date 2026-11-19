using System.Data.Common;
using Yasda.Shared.BuildingBlocks;
using AppRoles = Yasda.Shared.BuildingBlocks.Roles;   // "Roles" alone is IdentityDbContext.Roles, the table
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Yasda.Modules.Identity.Infrastructure;

internal sealed class YasdaIdentityDbContext(DbContextOptions<YasdaIdentityDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<long>, long>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    // The keys that encrypt sign-in cookies and tokens. Stored here so sessions survive restarts and are shared
    // by every instance of the app.
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<SignInAttempt> SignInAttempts => Set<SignInAttempt>();

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder o, DbConnection connection) =>
        o.UseNpgsql(connection, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", Schema))
         .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.HasDefaultSchema(Schema);

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.FullName).HasMaxLength(100);
        });
        b.Entity<IdentityUserClaim<long>>().ToTable("user_claims");
        b.Entity<IdentityUserLogin<long>>().ToTable("user_logins");
        b.Entity<IdentityUserToken<long>>().ToTable("user_tokens");
        b.Entity<IdentityUserRole<long>>().ToTable("user_roles");
        b.Entity<IdentityRoleClaim<long>>().ToTable("role_claims");
        b.Entity<IdentityRole<long>>().ToTable("roles").HasData(
            Role(1, AppRoles.Admin), Role(2, AppRoles.TeamLead), Role(3, AppRoles.Agent),
            Role(4, AppRoles.KbEditor), Role(5, AppRoles.Requester));
        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");

        b.Entity<SignInAttempt>(e =>
        {
            e.ToTable("sign_in_attempt");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Outcome).HasMaxLength(30);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.HasIndex(x => x.OccurredUtc);
            e.HasIndex(x => x.Email);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });
    }

    // Fixed stamps: seed data must be identical on every build, or each migration would "change" the roles.
    private static IdentityRole<long> Role(long id, string name) => new()
    {
        Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), ConcurrencyStamp = $"role-{id}"
    };
}

// For `dotnet ef migrations add`; never connects.
internal sealed class YasdaIdentityDbContextFactory : IDesignTimeDbContextFactory<YasdaIdentityDbContext>
{
    public YasdaIdentityDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<YasdaIdentityDbContext>();
        YasdaIdentityDbContext.Configure(o, new Npgsql.NpgsqlConnection("Host=localhost;Database=yasda_design_time"));
        return new YasdaIdentityDbContext(o.Options);
    }
}
