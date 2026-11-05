using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Erp.Integration.Tests;

// A production install: its own empty database, no development users, only the InitialAdmin settings.
public sealed class FirstRunFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "owner@company.test";
    public const string AdminPassword = "a long first-run password";

    private static readonly string ConnectionString =
        new NpgsqlConnectionStringBuilder(CustomWebAppFactory.TestConnectionString) { Database = "erp_test_first_run" }
            .ConnectionString;

    public FirstRunFactory() => CustomWebAppFactory.DropDatabase(ConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", ConnectionString);
        builder.UseSetting("Database:SeedDevelopmentData", "false");
        builder.UseSetting("InitialAdmin:Email", AdminEmail);
        builder.UseSetting("InitialAdmin:Password", AdminPassword);
        builder.UseSetting("ApiKeys:tests", CustomWebAppFactory.TestApiKey);
    }

    protected override void ConfigureClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Add("X-Api-Key", CustomWebAppFactory.TestApiKey);
        base.ConfigureClient(client);
    }
}

// The least-privilege roles can't create a database, so this one only runs against the default superuser.
public sealed class OwnDatabaseFactAttribute : FactAttribute
{
    public OwnDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ERP_TEST_DB_OWNER") is not null)
            Skip = "Needs to create its own database.";
    }
}

public class FirstRunTests(FirstRunFactory factory) : IClassFixture<FirstRunFactory>
{
    private sealed record Tokens(string AccessToken);
    private sealed record Me(long Id, string Username, string Role);

    [OwnDatabaseFact]
    public async Task A_fresh_install_gets_its_first_admin_from_configuration()
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/auth/login",
            new { email = FirstRunFactory.AdminEmail, password = FirstRunFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await login.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var me = await client.GetFromJsonAsync<Me>("/auth/me");
        me!.Role.Should().Be(Roles.Admin);
    }
}
