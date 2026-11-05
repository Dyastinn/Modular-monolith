using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Erp.Integration.Tests;

// The whole API in memory against its own database, dropped first so every run starts from migrations + seed.
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>
{
    public static readonly string TestConnectionString =
        Environment.GetEnvironmentVariable("ERP_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=erp_test;Username=postgres;Password=erp_dev_only";

    // Set to run the suite as the least-privilege erp_app (ERP_TEST_DB) against a database prepared by
    // deploy/postgres/create-roles.sql, with migrations run as erp_owner. That database is not dropped.
    private static readonly string? OwnerConnectionString = Environment.GetEnvironmentVariable("ERP_TEST_DB_OWNER");

    public const string Password = "Password123!";

    public CustomWebAppFactory()
    {
        if (OwnerConnectionString is null)
            DropDatabase(TestConnectionString);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", TestConnectionString);
        if (OwnerConnectionString is not null)
            builder.UseSetting("ConnectionStrings:ErpDbMigrations", OwnerConnectionString);
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AuthRequestsPerMinute", "100000");
        builder.UseSetting("Outbox:PollIntervalMs", "200");
    }

    public sealed record Tokens(string AccessToken, string RefreshToken);

    public async Task<Tokens> LoginAsync(HttpClient client, string email, string password = Password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Tokens>())!;
    }

    public async Task<HttpClient> ClientFor(string email)
    {
        var client = CreateClient();
        var tokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return client;
    }

    public static void DropDatabase(string connectionString)
    {
        var target = new NpgsqlConnectionStringBuilder(connectionString);
        var admin = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres", Pooling = false };
        using var conn = new NpgsqlConnection(admin.ConnectionString);
        conn.Open();
        using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{target.Database}\" WITH (FORCE)", conn);
        drop.ExecuteNonQuery();
    }
}
