using AwesomeAssertions;
using Dapper;
using Erp.Modules.Sales.Domain;
using Erp.Modules.Sales.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Integration.Tests;

// What the migrations built, checked in the database itself.
[Collection("Integration")]
public class DatabaseTests(CustomWebAppFactory factory)
{
    // Every module that registered a database, so the tests grow with the app without being edited.
    // A module's schema is its name in lower case: Sales → sales.
    private string[] Schemas
    {
        get
        {
            using var scope = factory.Services.CreateScope();
            return scope.ServiceProvider.GetServices<IModuleDatabase>().Select(m => m.Module.ToLowerInvariant()).ToArray();
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

        histories.Should().BeEquivalentTo(Schemas).And.Contain("sales");
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

    // The app seeds on every development start, so the seed must be safe to run again.
    [Fact]
    public async Task Seeding_twice_changes_nothing()
    {
        await using var conn = factory.OpenDatabase();
        // Every row in every module's tables, except the outbox's: what the seed could possibly have added to.
        var counts = "SELECT 0" + string.Concat((await conn.QueryAsync<string>(
            """
            SELECT quote_ident(table_schema) || '.' || quote_ident(table_name) FROM information_schema.tables
            WHERE table_schema = ANY(@Schemas) AND table_type = 'BASE TABLE' AND table_name <> 'outbox_message'
            """, new { Schemas })).Select(t => $" + (SELECT COUNT(*) FROM {t})"));
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

    // Value converters turn ids, Money and Quantity into columns and back. A wrong one corrupts silently.
    [Fact]
    public async Task Ids_money_and_quantities_survive_the_trip_to_the_database_and_back()
    {
        SalesOrderId id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            var order = SalesOrder.Create("SO-ROUNDTRIP", new CustomerId(5), new UserId(1), DateTime.UtcNow);
            var wire = await db.SalesItems.Include(i => i.Category).SingleAsync(i => i.Id == new ItemId(5));   // by the kg
            order.SetLines([(wire, new Quantity(1.234m))], Roles.SalesRep);
            db.SalesOrders.Add(order);
            await db.SaveChangesAsync();
            id = order.Id;
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
            var loaded = await db.SalesOrders.SingleAsync(o => o.Id == id);

            loaded.CustomerId.Should().Be(new CustomerId(5));
            loaded.Lines.Single().Quantity.Should().Be(new Quantity(1.234m));
            loaded.Lines.Single().UnitPrice.Should().Be(new Money(620.50m));
            loaded.Total.Should().Be(new Money(765.70m));   // 620.50 × 1.234 = 765.697, rounded to centavos
        }
    }
}
