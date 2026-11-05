using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Erp.Modules.Identity.Infrastructure;
using Erp.Shared.BuildingBlocks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Integration.Tests;

[Collection("Integration")]
public class AuthTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Wrong_password_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/login",
            new { email = "rep@erp.local", password = "not-the-password" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record Me(long Id, string Username, string Role);

    [Fact]
    public async Task Me_returns_the_signed_in_user_and_role()
    {
        var me = await (await factory.ClientFor("manager@erp.local")).GetFromJsonAsync<Me>("/auth/me");

        me!.Username.Should().Be("manager@erp.local");
        me.Role.Should().Be(Roles.SalesManager);
    }

    [Fact]
    public async Task Refresh_issues_a_new_access_token()
    {
        var client = factory.CreateClient();
        var tokens = await factory.LoginAsync(client, "rep@erp.local");

        var refreshed = await client.PostAsJsonAsync("/auth/refresh", new { tokens.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Self_registration_is_disabled()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/auth/register",
            new { email = "intruder@example.com", password = "LongEnoughPassword1" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = await CreateUser();
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/auth/login", new { email, password = "wrong-password!" });

        var withRightPassword = await client.PostAsJsonAsync("/auth/login", new { email, password = CustomWebAppFactory.Password });

        withRightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_everywhere_kills_existing_refresh_tokens()
    {
        var email = await CreateUser();
        var client = factory.CreateClient();
        var tokens = await factory.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        (await client.PostAsync("/auth/logout-everywhere", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/auth/refresh", new { tokens.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> CreateUser()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"user-{Guid.NewGuid():N}@erp.local";
        var user = new AppUser { UserName = email, Email = email, EmailConfirmed = true };
        (await users.CreateAsync(user, CustomWebAppFactory.Password)).Succeeded.Should().BeTrue();
        (await users.AddToRoleAsync(user, Roles.SalesRep)).Succeeded.Should().BeTrue();
        return email;
    }
}
