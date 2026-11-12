using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.PostgreSql;

namespace Erp.Integration.Tests;

// A production install: its own empty server, no development users, only the InitialAdmin settings.
public sealed class FirstRunFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "owner@company.test";
    public const string AdminPassword = "a long first-run password";

    private readonly PostgreSqlContainer _postgres = TestDatabase.Container().Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:ErpDb", _postgres.GetConnectionString());
        builder.UseSetting("Database:SeedDevelopmentData", "false");
        builder.UseSetting("InitialAdmin:Email", AdminEmail);
        builder.UseSetting("InitialAdmin:Password", AdminPassword);
    }
}

public class FirstRunTests(FirstRunFactory factory) : IClassFixture<FirstRunFactory>
{
    private sealed record Tokens(string AccessToken);
    private sealed record Me(long Id, string Username, string Role);

    [Fact]
    public async Task A_fresh_install_gets_its_first_admin_from_configuration()
    {
        var client = factory.CreateClient();

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = FirstRunFactory.AdminEmail, password = FirstRunFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        var token = (await login.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");
        me!.Role.Should().Be(Roles.Admin);
    }

    [Fact]
    public async Task A_fresh_install_has_no_development_users()
    {
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "admin@erp.local", password = CustomWebAppFactory.Password });
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
