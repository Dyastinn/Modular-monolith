using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Yasda.Integration.Tests;

[Collection("Integration")]
public class UserAdministrationTests(CustomWebAppFactory factory)
{
    private sealed record Created(long Id);
    private sealed record Me(long Id, string Username, string FullName, string Role, bool MustChangePassword);
    private sealed record Page<T>(List<T> Items, int TotalCount);
    private sealed record Attempt(string Email, string Outcome);
    private sealed record UserRow(long Id, string Email, string FullName, string Role, bool IsDisabled, bool MustChangePassword);

    private const string Temporary = "a temporary password";

    [Fact]
    public async Task A_new_user_must_change_the_temporary_password_and_a_disabled_user_is_refused()
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        var email = $"staff-{Guid.NewGuid():N}@yasda.local";

        var created = await admin.PostAsJsonAsync("/api/users",
            new { email, fullName = "Lorna Garcia", role = "Agent", temporaryPassword = Temporary });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<Created>())!.Id;
        (await admin.GetFromJsonAsync<UserRow>($"/api/users/{id}")).Should()
            .Be(new UserRow(id, email, "Lorna Garcia", "Agent", IsDisabled: false, MustChangePassword: true));

        var user = factory.CreateClient();
        var tokens = await CustomWebAppFactory.LoginAsync(user, email, Temporary);
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        (await user.GetFromJsonAsync<Me>("/api/auth/me"))!.MustChangePassword.Should().BeTrue();

        (await user.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = Temporary, newPassword = "my own long password" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await user.GetFromJsonAsync<Me>("/api/auth/me"))!.MustChangePassword.Should().BeFalse();

        (await admin.PostAsync($"/api/users/{id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password = "my own long password" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var attempts = await admin.GetFromJsonAsync<Page<Attempt>>($"/api/sign-in-attempts?email={email}");
        attempts!.Items.Select(a => a.Outcome).Should().Contain(["Succeeded", "NotAllowed"]);
    }

    [Fact]
    public async Task Only_an_admin_manages_users_and_nobody_disables_themselves()
    {
        var agent = await factory.ClientFor("agent@yasda.local");
        (await agent.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await factory.ClientFor("admin@yasda.local");
        var me = await admin.GetFromJsonAsync<Me>("/api/auth/me");
        (await admin.PostAsync($"/api/users/{me!.Id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task An_unknown_user_is_404()
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        (await admin.GetAsync("/api/users/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_weak_temporary_password_is_refused_by_the_policy()
    {
        var admin = await factory.ClientFor("admin@yasda.local");
        var response = await admin.PostAsJsonAsync("/api/users",
            new { email = $"weak-{Guid.NewGuid():N}@yasda.local", fullName = "Weak", role = "Requester", temporaryPassword = "short" });
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
