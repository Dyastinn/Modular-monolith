using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Erp.Integration.Tests;

// Boots the whole API in memory against its OWN database (erp_test, never the dev database). The database is
// dropped first, so every run starts from the migrations + dev seed and no test inherits another run's rows.
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    // Override with the ERP_TEST_DB environment variable (e.g. on a build server).
    public static readonly string TestConnectionString =
        Environment.GetEnvironmentVariable("ERP_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=erp_test;Username=postgres;Password=erp_dev_only";

    // The ApiKeyMiddleware gates EVERY request, including /auth/login. Tests are a client app like any other,
    // so they get their own key rather than an exemption.
    public const string TestApiKey = "test-suite-key";

    public CustomWebAppFactory() => DropTestDatabase();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // migrate + seed on startup, dev signing key
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);
        builder.UseSetting("ApiKeys:tests", TestApiKey);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", TestApiKey);
        base.ConfigureClient(client);
    }

    private static void DropTestDatabase()
    {
        var target = new NpgsqlConnectionStringBuilder(TestConnectionString);
        var admin = new NpgsqlConnectionStringBuilder(TestConnectionString) { Database = "postgres", Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE)", conn);
        drop.ExecuteNonQuery();
    }
}
