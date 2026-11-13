using System.Net.Http.Headers;
using System.Net.Http.Json;
using Erp.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Erp.Integration.Tests;

// The whole API in memory against a throwaway PostgreSQL in a container: every run starts from an empty server, with
// the same two roles as production. Migrations run as erp_owner; the app and the tests connect as erp_app.
public sealed class CustomWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Password = "Password123!";
    private const string Database = "erp_test", OwnerPassword = "owner-test-only", AppPassword = "app-test-only";

    private readonly PostgreSqlContainer _postgres = TestDatabase.Container()
        .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "create-roles.sql")), "/erp/")
        .Build();

    public string ConnectionString { get; private set; } = default!;        // erp_app: rows only
    public string OwnerConnectionString { get; private set; } = default!;   // erp_owner: runs the migrations

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        var roles = await _postgres.ExecAsync(["psql", "-U", "postgres", "-v", "ON_ERROR_STOP=1", "-v", $"db={Database}",
            "-v", $"owner_password={OwnerPassword}", "-v", $"app_password={AppPassword}", "-f", "/erp/create-roles.sql"]);
        if (roles.ExitCode != 0) throw new InvalidOperationException($"create-roles.sql failed: {roles.Stderr}");

        var server = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = Database };
        OwnerConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString)
            { Username = "erp_owner", Password = OwnerPassword }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(server.ConnectionString)
            { Username = "erp_app", Password = AppPassword }.ConnectionString;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", ConnectionString);
        builder.UseSetting("ConnectionStrings:ErpDbMigrations", OwnerConnectionString);
        builder.UseSetting("RateLimiting:RequestsPerMinute", "100000");
        builder.UseSetting("RateLimiting:AuthRequestsPerMinute", "100000");
        builder.UseSetting("Outbox:PollIntervalMs", "200");
    }

    public sealed record Tokens(string AccessToken, string RefreshToken);

    public static async Task<Tokens> LoginAsync(HttpClient client, string email, string password = Password)
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

    // A throwaway staff member, for tests that lock, disable or sign out a user, or need a second person in a role.
    public async Task<string> CreateUserAsync(string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"user-{Guid.NewGuid():N}@erp.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true, FullName = "Test User" };
        var created = await users.CreateAsync(user, Password);
        if (!created.Succeeded) throw new InvalidOperationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return email;
    }

    // Starts the app first if no test has yet: the app is what migrates the database.
    public NpgsqlConnection OpenDatabase()
    {
        _ = Server;
        return new(ConnectionString);
    }
}

public static class TestDatabase
{
    // Fully qualified, so Podman doesn't ask which registry "postgres" means. The version production runs.
    public const string Image = "docker.io/library/postgres:18";

    public static PostgreSqlBuilder Container() => new PostgreSqlBuilder(Image);
}
