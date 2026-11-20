using AwesomeAssertions;
using Dapper;
using Yasda.Modules.Tickets.Domain;
using Yasda.Modules.Tickets.Infrastructure;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Yasda.Integration.Tests;

// What the migrations built, checked in the database itself.
[Collection("Integration")]
public class DatabaseTests(CustomWebAppFactory factory)
{
    // Every module that registered a database, so the tests grow with the app without being edited.
    // A module's schema is its name in lower case: Identity → identity.
    private string[] Schemas
    {
        get
        {
            using var scope = factory.Services.CreateScope();
            var modules = scope.ServiceProvider.GetServices<IModuleDatabase>();
            return modules.Select(m => m.Module.ToLowerInvariant()).ToArray();
        }
    }

    // The tests' container runs the image production runs. Upgrading PostgreSQL is a decision, not an accident.
    [Fact]
    public async Task The_tests_run_on_the_postgresql_version_production_runs()
    {
        await using var conn = factory.OpenDatabase();
        (await conn.ExecuteScalarAsync<string>("SHOW server_version")).Should().StartWith("18.");
    }

    [Fact]
    public async Task Every_module_has_its_own_schema_and_migration_history()
    {
        await using var conn = factory.OpenDatabase();
        var histories = (await conn.QueryAsync<string>(
            "SELECT table_schema FROM information_schema.tables WHERE table_name = '__EFMigrationsHistory'")).ToList();

        histories.Should().BeEquivalentTo(Schemas).And.Contain("identity");
    }

    [Fact]
    public async Task Nothing_of_ours_lands_in_the_public_schema()
    {
        await using var conn = factory.OpenDatabase();
        var tables = await conn.QueryAsync<string>("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'");
        var functions = await conn.QueryAsync<string>(
            "SELECT p.proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public'");

        tables.Should().BeEmpty();
        functions.Should().BeEmpty("read functions are schema-qualified in their definition");
    }

    [Fact]
    public async Task Tables_and_columns_are_snake_case()
    {
        await using var conn = factory.OpenDatabase();
        var names = await conn.QueryAsync<string>(
            """
            SELECT table_schema || '.' || table_name || '.' || column_name FROM information_schema.columns
            WHERE table_schema = ANY(@Schemas) AND table_name <> '__EFMigrationsHistory'
              AND (table_name ~ '[A-Z]' OR column_name ~ '[A-Z]')
            """, new { Schemas = Schemas });

        names.Should().BeEmpty("EFCore.NamingConventions maps every name to snake_case");
    }

    // Modules are islands: a foreign key into another module's schema would tie them together in the database.
    [Fact]
    public async Task No_foreign_key_crosses_a_module_boundary()
    {
        await using var conn = factory.OpenDatabase();
        var crossing = await conn.QueryAsync<string>(
            """
            SELECT c.conname FROM pg_constraint c
            JOIN pg_class src ON src.oid = c.conrelid   JOIN pg_namespace s ON s.oid = src.relnamespace
            JOIN pg_class dst ON dst.oid = c.confrelid  JOIN pg_namespace d ON d.oid = dst.relnamespace
            WHERE c.contype = 'f' AND s.nspname <> d.nspname
            """);

        crossing.Should().BeEmpty();
    }

    // A status the database doesn't know is refused by the database itself, whatever the code does.
    [Fact]
    public async Task The_database_refuses_a_status_it_does_not_know()
    {
        await using var conn = factory.OpenDatabase();

        var insert = () => conn.ExecuteAsync(
            """
            INSERT INTO tickets.ticket (subject, priority, status, requester_id, requester_name, created_utc, updated_utc)
            VALUES ('x', 'Normal', 'Banana', 1, 'Rita', now(), now())
            """);

        var failure = await insert.Should().ThrowAsync<PostgresException>();
        failure.Which.ConstraintName.Should().Be("ck_ticket_status");
    }

    // Written in one scope and read in another: a new DbContext remembers nothing, so the data really comes back
    // from the database.
    [Fact]
    public async Task A_ticket_and_its_conversation_survive_a_round_trip()
    {
        var now = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
        long id;
        await using (var writeScope = factory.Services.CreateAsyncScope())
        {
            var db = writeScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
            var ticket = Ticket.Create("Round trip", "First words", TicketPriority.High, new UserId(5), "Rita Cruz", now);
            ticket.Take(new UserId(3), "Alan Reyes", now);
            ticket.Reply(new UserId(3), "Alan Reyes", "A note for us", isInternal: true, authorIsStaff: true, now);
            db.Tickets.Add(ticket);
            await db.SaveChangesAsync();
            id = ticket.Id.Value;
        }

        await using var readScope = factory.Services.CreateAsyncScope();
        var reader = readScope.ServiceProvider.GetRequiredService<TicketsDbContext>();
        var loaded = await reader.Tickets.FindAsync(new TicketId(id));

        loaded.Should().NotBeNull();
        loaded.Subject.Should().Be("Round trip");
        loaded.Priority.Should().Be(TicketPriority.High);
        loaded.Status.Should().Be(TicketStatus.Open);
        loaded.AssigneeId.Should().Be(new UserId(3));
        loaded.Messages.Should().HaveCount(2);
        loaded.Messages[1].IsInternal.Should().BeTrue();
    }

    // The app seeds on every development start, so the seed must be safe to run again.
    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var conn = factory.OpenDatabase();
        // Every row in every module's tables, except the outbox's: what the seed could possibly have added to.
        var tables = await conn.QueryAsync<string>(
            """
            SELECT quote_ident(table_schema) || '.' || quote_ident(table_name) FROM information_schema.tables
            WHERE table_schema = ANY(@Schemas) AND table_type = 'BASE TABLE' AND table_name <> 'outbox_message'
            """, new { Schemas });

        // One query adding up every table's row count: SELECT 0 + (SELECT COUNT(*) FROM a) + (SELECT COUNT(*) FROM b) ...
        var counts = "SELECT 0";
        foreach (var table in tables)
            counts += $" + (SELECT COUNT(*) FROM {table})";
        var before = await conn.ExecuteScalarAsync<long>(counts);

        await using (var scope = factory.Services.CreateAsyncScope())
        await using (var owner = new NpgsqlConnection(factory.OwnerConnectionString))
        {
            await owner.OpenAsync();
            foreach (var module in scope.ServiceProvider.GetServices<IModuleDatabase>())
                await module.SeedDevelopmentDataAsync(owner, default);
        }

        (await conn.ExecuteScalarAsync<long>(counts)).Should().Be(before);
    }
}
