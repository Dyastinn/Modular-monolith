using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;
using Erp.Shared.BuildingBlocks;

namespace Erp.Integration.Tests;

[Collection("Integration")]
public class AuthTests(CustomWebAppFactory factory)
{
    [Fact]
    public async Task Wrong_password_is_401()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "rep@erp.local", password = "not-the-password" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record Me(long Id, string Username, string Role);
    private sealed record LoginProblem(string? Title, int Status, string? Detail);

    [Fact]
    public async Task Me_returns_the_signed_in_user_and_role()
    {
        var me = await (await factory.ClientFor("manager@erp.local")).GetFromJsonAsync<Me>("/api/auth/me");

        me!.Username.Should().Be("manager@erp.local");
        me.Role.Should().Be(Roles.SalesManager);
    }

    [Fact]
    public async Task Refresh_issues_a_new_access_token()
    {
        var client = factory.CreateClient();
        var tokens = await CustomWebAppFactory.LoginAsync(client, "rep@erp.local");

        var refreshed = await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken });

        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Self_registration_is_disabled()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { email = "intruder@example.com", password = "LongEnoughPassword1" });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = await CreateUser();
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password!" });

        var withRightPassword = await client.PostAsJsonAsync("/api/auth/login", new { email, password = CustomWebAppFactory.Password });

        withRightPassword.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_everywhere_kills_existing_refresh_tokens()
    {
        var email = await CreateUser();
        var client = factory.CreateClient();
        var tokens = await CustomWebAppFactory.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        (await client.PostAsync("/api/auth/logout-everywhere", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { tokens.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Same status, same body: an attacker must not learn which emails are staff accounts.
    [Fact]
    public async Task An_unknown_email_gets_the_same_answer_as_a_wrong_password()
    {
        var client = factory.CreateClient();
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@erp.local", password = "whatever-123" });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email = "rep@erp.local", password = "whatever-123" });

        unknown.StatusCode.Should().Be(wrong.StatusCode);
        (await unknown.Content.ReadFromJsonAsync<LoginProblem>()).Should().Be(await wrong.Content.ReadFromJsonAsync<LoginProblem>(),
            "only the trace id may differ");
    }

    [Fact]
    public async Task The_email_is_not_case_sensitive()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new { email = "REP@ERP.LOCAL", password = CustomWebAppFactory.Password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_successful_sign_in_resets_the_count_of_failures()
    {
        var email = await CreateUser();
        var client = factory.CreateClient();
        async Task Fail(int times)
        {
            for (var i = 0; i < times; i++)
                await client.PostAsJsonAsync("/api/auth/login", new { email, password = "wrong-password!" });
        }

        await Fail(4);
        await CustomWebAppFactory.LoginAsync(client, email);
        await Fail(4);

        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = CustomWebAppFactory.Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK, "4 + 4 failures with a success between is not 5 in a row");
    }

    [Fact]
    public async Task A_made_up_refresh_token_is_refused()
    {
        var response = await factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken = "not-a-token" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The browser app gets a 401 to react to, never a redirect to a login page the API doesn't have.
    [Theory]
    [InlineData("/api/sales-orders")]
    [InlineData("/api/auth/me")]
    public async Task Calling_the_api_without_signing_in_is_401_not_a_redirect(string path)
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_browser_signs_in_with_a_cookie()
    {
        var client = factory.CreateClient();   // keeps cookies, like a browser
        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true",
            new { email = "rep@erp.local", password = CustomWebAppFactory.Password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_role_without_access_gets_403_not_401()
    {
        var clerk = await factory.ClientFor("clerk@erp.local");
        (await clerk.PostAsJsonAsync("/api/sales-orders", new { customerId = 1, lines = new[] { new { itemId = 1, quantity = 1 } } }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Task<string> CreateUser() => factory.CreateUserAsync(Roles.SalesRep);
}
