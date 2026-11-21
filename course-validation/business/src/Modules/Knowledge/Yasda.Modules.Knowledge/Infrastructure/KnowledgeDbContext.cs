using System.Data.Common;
using Yasda.Modules.Knowledge.Domain;
using Yasda.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Yasda.Modules.Knowledge.Infrastructure;

internal sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options)
{
    public const string Schema = "knowledge";

    public DbSet<Article> Articles => Set<Article>();

    // Shared by the app and `dotnet ef`, so the two can't drift apart.
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, DbConnection connection)
    {
        return options
            // An article loads three lists (versions, edit list, history). One query joining them all would repeat every row of each
            // list for every row of the others, so each list is read with its own query.
            .UseNpgsql(connection, npg => npg
                .MigrationsHistoryTable("__EFMigrationsHistory", Schema)
                .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery))
            .UseSnakeCaseNamingConvention();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<ArticleId>().HaveConversion<ArticleIdConverter>();
        c.Properties<UserId>().HaveConversion<UserIdConverter>();
        // Statuses and the like are stored by name: readable in SQL, and safe if an enum is ever reordered.
        c.Properties<ArticleStatus>().HaveConversion<string>().HaveMaxLength(20);
        c.Properties<ArticleVisibility>().HaveConversion<string>().HaveMaxLength(20);
        c.Properties<ArticleAction>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);

        // Knowledge raises no events yet, but the dispatcher, the backlog gauges and the health check look in every module's schema
        // for this table, so it is here from the start.
        b.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_message");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(500);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.TraceParent).HasMaxLength(55);   // a W3C traceparent is always 55 characters
            e.HasIndex(x => x.OccurredUtc).HasFilter("processed_utc IS NULL");
        });

        b.Entity<Article>(e =>
        {
            e.ToTable("article", t =>
            {
                t.HasCheckConstraint("ck_article_status", "status IN ('Draft', 'InReview', 'Published', 'Archived')");
                t.HasCheckConstraint("ck_article_visibility", "visibility IN ('Public', 'Internal')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityColumn();
            e.Property(x => x.Title).HasMaxLength(Article.TitleMaxLength);
            e.Property(x => x.Summary).HasMaxLength(Article.SummaryMaxLength);
            e.Property(x => x.Body).HasMaxLength(Article.BodyMaxLength);
            e.Property(x => x.CreatedByName).HasMaxLength(100);
            e.Property(x => x.LastEditedByName).HasMaxLength(100);
            e.Property(x => x.RejectionReason).HasMaxLength(Article.ReasonMaxLength);
            e.HasIndex(x => new { x.Status, x.UpdatedUtc });
            e.HasIndex(x => x.CreatedById);
            e.Property<uint>("RowVersion").IsRowVersion().HasColumnName("xmin");
            e.Ignore(x => x.DomainEvents);

            e.OwnsMany(x => x.Versions, v =>
            {
                v.ToTable("article_version");
                v.WithOwner().HasForeignKey("ArticleId");
                v.HasKey(x => x.Id);
                v.Property(x => x.Id).UseIdentityColumn();
                v.Property(x => x.Title).HasMaxLength(Article.TitleMaxLength);
                v.Property(x => x.Summary).HasMaxLength(Article.SummaryMaxLength);
                v.Property(x => x.Body).HasMaxLength(Article.BodyMaxLength);
                v.Property(x => x.PublishedByName).HasMaxLength(100);
                // Backstop: two people publishing at once can't both write version 3.
                v.HasIndex("ArticleId", nameof(ArticleVersion.Number)).IsUnique();
            });
            e.Navigation(x => x.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);

            e.OwnsMany(x => x.Editors, ed =>
            {
                ed.ToTable("article_editor");
                ed.WithOwner().HasForeignKey("ArticleId");
                ed.HasKey(x => x.Id);
                ed.Property(x => x.Id).UseIdentityColumn();
                ed.Property(x => x.UserName).HasMaxLength(100);
                ed.Property(x => x.AddedByName).HasMaxLength(100);
                // Backstop: a person is on an article's edit list once.
                ed.HasIndex("ArticleId", nameof(ArticleEditor.UserId)).IsUnique();
            });
            e.Navigation(x => x.Editors).HasField("_editors").UsePropertyAccessMode(PropertyAccessMode.Field);

            e.OwnsMany(x => x.History, h =>
            {
                h.ToTable("article_history");
                h.WithOwner().HasForeignKey("ArticleId");
                h.HasKey(x => x.Id);
                h.Property(x => x.Id).UseIdentityColumn();
                h.Property(x => x.ActorName).HasMaxLength(100);
                h.Property(x => x.Note).HasMaxLength(Article.ReasonMaxLength);
            });
            e.Navigation(x => x.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        b.Entity<ArticleViewDay>(e =>
        {
            e.ToTable("article_view_day");
            e.HasKey(x => new { x.ArticleId, x.Day });
            e.HasOne<Article>().WithMany().HasForeignKey(x => x.ArticleId);
        });
    }
}

internal sealed class ArticleIdConverter() : ValueConverter<ArticleId, long>(v => v.Value, v => new ArticleId(v));
internal sealed class UserIdConverter() : ValueConverter<UserId, long>(v => v.Value, v => new UserId(v));

// For `dotnet ef migrations add`; never connects.
internal sealed class KnowledgeDbContextFactory : IDesignTimeDbContextFactory<KnowledgeDbContext>
{
    public KnowledgeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<KnowledgeDbContext>();
        KnowledgeDbContext.Configure(options, new Npgsql.NpgsqlConnection("Host=localhost;Database=yasda_design_time"));
        return new KnowledgeDbContext(options.Options);
    }
}
