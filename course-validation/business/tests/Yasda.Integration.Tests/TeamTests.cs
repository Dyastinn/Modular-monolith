using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;

namespace Yasda.Integration.Tests;

// Teams belong to Identity: admins manage them and who is in them, and only staff can be in one. Every test makes its own
// team (with a name no other has) because all the tests share one database.
[Collection("Integration")]
public class TeamTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local", Requester = "requester@yasda.local";

    private sealed record Created(long Id);
    private sealed record Me(long Id, string FullName);
    private sealed record Member(long UserId, string FullName, string Role, bool Notify, bool IsDisabled);
    private sealed record TeamView(long Id, string Name, bool IsActive, string NotifyMode, List<Member> Members);
    private sealed record TeamRow(long Id, string Name, bool IsActive, string NotifyMode, long MemberCount);
    private sealed record Page(List<TeamRow> Items, int TotalCount);
    private sealed record Staff(long Id, string FullName, string Role);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private async Task<long> IdOf(string email)
    {
        var client = await factory.ClientFor(email);
        var me = await client.GetFromJsonAsync<Me>("/api/auth/me");
        return me!.Id;
    }

    private static async Task<long> CreateTeam(HttpClient admin, string? name = null)
    {
        var response = await admin.PostAsJsonAsync("/api/teams", new { name = name ?? $"Team {Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<Created>();
        return created!.Id;
    }

    private static async Task AddMember(HttpClient admin, long teamId, long userId)
    {
        var response = await admin.PostAsJsonAsync($"/api/teams/{teamId}/members", new { userId });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task An_admin_creates_a_team_and_finds_it_in_the_list()
    {
        var admin = await factory.ClientFor(Admin);
        var name = $"Billing {Guid.NewGuid():N}";

        var response = await admin.PostAsJsonAsync("/api/teams", new { name = $"  {name}  " });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;
        response.Headers.Location!.ToString().Should().EndWith($"/api/teams/{id}");
        var found = await admin.GetFromJsonAsync<Page>($"/api/teams?search={name}");
        found!.Items.Should().ContainSingle().Which.Should().Be(new TeamRow(id, name, true, "LeadsOnly", 0));
    }

    [Fact]
    public async Task A_name_another_team_has_is_refused_whatever_the_case()
    {
        var admin = await factory.ClientFor(Admin);
        var name = $"Unique {Guid.NewGuid():N}";
        await CreateTeam(admin, name);

        var again = await admin.PostAsJsonAsync("/api/teams", new { name = name.ToUpperInvariant() });

        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(again)).Should().Be("IDENTITY-0007");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_team_without_a_name_is_refused_before_the_handler(string name)
    {
        var admin = await factory.ClientFor(Admin);

        var response = await admin.PostAsJsonAsync("/api/teams", new { name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("YASDA-0001");
    }

    [Fact]
    public async Task A_team_is_renamed_and_a_taken_name_is_refused()
    {
        var admin = await factory.ClientFor(Admin);
        var id = await CreateTeam(admin);
        var other = $"Other {Guid.NewGuid():N}";
        await CreateTeam(admin, other);
        var renamed = $"Renamed {Guid.NewGuid():N}";

        (await admin.PutAsJsonAsync($"/api/teams/{id}", new { name = renamed })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var refused = await admin.PutAsJsonAsync($"/api/teams/{id}", new { name = other });

        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}"))!.Name.Should().Be(renamed);
        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("IDENTITY-0007");
    }

    [Fact]
    public async Task A_team_can_be_deactivated_and_activated_again()
    {
        var admin = await factory.ClientFor(Admin);
        var id = await CreateTeam(admin);

        (await admin.PostAsync($"/api/teams/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}"))!.IsActive.Should().BeFalse();

        (await admin.PostAsync($"/api/teams/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}"))!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Staff_are_added_to_a_team_and_removed_again()
    {
        var admin = await factory.ClientFor(Admin);
        var id = await CreateTeam(admin);
        var agent = await IdOf(Agent);

        await AddMember(admin, id, agent);
        await AddMember(admin, id, agent);   // twice changes nothing

        var team = await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}");
        team!.Members.Should().ContainSingle().Which.Should().Be(new Member(agent, "Alan Reyes", "Agent", false, false));

        (await admin.DeleteAsync($"/api/teams/{id}/members/{agent}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}"))!.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task A_requester_cannot_join_a_team()
    {
        var admin = await factory.ClientFor(Admin);
        var id = await CreateTeam(admin);
        var requester = await IdOf(Requester);

        var response = await admin.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = requester });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("IDENTITY-0008");
        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{id}"))!.Members.Should().BeEmpty();
    }

    [Fact]
    public async Task A_disabled_account_cannot_join_a_team()
    {
        var admin = await factory.ClientFor(Admin);
        var id = await CreateTeam(admin);
        var email = await factory.CreateUserAsync(Roles.Agent);
        var userId = await IdOf(email);
        (await admin.PostAsync($"/api/users/{userId}/disable", null)).EnsureSuccessStatusCode();

        var response = await admin.PostAsJsonAsync($"/api/teams/{id}/members", new { userId });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(response)).Should().Be("IDENTITY-0008");
    }

    [Fact]
    public async Task Only_admins_manage_teams_and_staff_may_read_them()
    {
        var lead = await factory.ClientFor(Lead);
        var agent = await factory.ClientFor(Agent);
        var requester = await factory.ClientFor(Requester);

        (await lead.GetAsync("/api/teams")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await agent.GetAsync("/api/teams")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await requester.GetAsync("/api/teams")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (var client in new[] { lead, agent, requester })
        {
            var response = await client.PostAsJsonAsync("/api/teams", new { name = $"Sneaky {Guid.NewGuid():N}" });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task A_team_that_does_not_exist_is_404()
    {
        var admin = await factory.ClientFor(Admin);

        (await admin.GetAsync("/api/teams/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync("/api/teams/999999999", new { name = "Nothing" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/api/teams/999999999/members", new { userId = 1 })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_teams_list_sorts_and_pages_through_the_address()
    {
        var admin = await factory.ClientFor(Admin);
        var word = Guid.NewGuid().ToString("N");
        var small = await CreateTeam(admin, $"{word} small");
        var big = await CreateTeam(admin, $"{word} big");
        await AddMember(admin, big, await IdOf(Agent));
        await AddMember(admin, big, await IdOf(Lead));

        var fewestFirst = await admin.GetFromJsonAsync<Page>($"/api/teams?search={word}&sort=members");
        var mostFirst = await admin.GetFromJsonAsync<Page>($"/api/teams?search={word}&sort=members&desc=true");
        var byNameDescending = await admin.GetFromJsonAsync<Page>($"/api/teams?search={word}&sort=name&desc=true");

        fewestFirst!.Items.Select(t => t.Id).Should().Equal(small, big);
        mostFirst!.Items.Select(t => t.Id).Should().Equal(big, small);
        byNameDescending!.Items.Select(t => t.Name).Should().Equal($"{word} small", $"{word} big");
        (await admin.GetAsync("/api/teams?sort=notAColumn")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_staff_list_can_be_narrowed_to_one_team()
    {
        var admin = await factory.ClientFor(Admin);
        var agent = await factory.ClientFor(Agent);
        var id = await CreateTeam(admin);
        var agentId = await IdOf(Agent);
        await AddMember(admin, id, agentId);

        var inTeam = await agent.GetFromJsonAsync<List<Staff>>($"/api/staff?teamId={id}");
        var everyone = await agent.GetFromJsonAsync<List<Staff>>("/api/staff");

        inTeam!.Select(s => s.Id).Should().Equal(agentId);
        everyone!.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task The_development_teams_are_seeded()
    {
        var admin = await factory.ClientFor(Admin);
        var serviceDesk = await admin.GetFromJsonAsync<Page>("/api/teams?search=Service desk");
        var network = await admin.GetFromJsonAsync<Page>("/api/teams?search=Network");

        serviceDesk!.Items.Should().Contain(t => t.Name == "Service desk" && t.MemberCount == 2);
        network!.Items.Should().Contain(t => t.Name == "Network" && t.MemberCount == 1);
    }

    // ---- ITeamDirectory: what other modules ask ----

    [Fact]
    public async Task The_directory_finds_a_team_by_number_and_by_name_in_any_case()
    {
        var admin = await factory.ClientFor(Admin);
        var name = $"Findable {Guid.NewGuid():N}";
        var id = await CreateTeam(admin, name);
        await using var scope = factory.Services.CreateAsyncScope();
        var teams = scope.ServiceProvider.GetRequiredService<ITeamDirectory>();

        (await teams.FindTeamAsync(id, CancellationToken.None)).Should().Be(new TeamInfo(id, name, true));
        (await teams.TeamIdOfAsync(name.ToUpperInvariant(), CancellationToken.None)).Should().Be(id);
        (await teams.FindTeamAsync(999999999, CancellationToken.None)).Should().BeNull();
        (await teams.TeamIdOfAsync("No such team", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task The_directory_knows_who_is_a_member_and_of_which_teams()
    {
        var admin = await factory.ClientFor(Admin);
        var first = await CreateTeam(admin);
        var second = await CreateTeam(admin);
        var agent = await IdOf(Agent);
        var lead = await IdOf(Lead);
        await AddMember(admin, first, agent);
        await AddMember(admin, second, agent);
        await using var scope = factory.Services.CreateAsyncScope();
        var teams = scope.ServiceProvider.GetRequiredService<ITeamDirectory>();

        (await teams.IsMemberAsync(first, agent, CancellationToken.None)).Should().BeTrue();
        (await teams.IsMemberAsync(first, lead, CancellationToken.None)).Should().BeFalse();
        (await teams.TeamIdsOfAsync(agent, CancellationToken.None)).Should().Contain([first, second]);
        (await teams.TeamIdsOfAsync(lead, CancellationToken.None)).Should().NotContain([first, second]);
    }

    // ---- Who hears about unowned work (decision D9) ----

    private async Task<IReadOnlyList<long>> NotifyList(long? teamId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var teams = scope.ServiceProvider.GetRequiredService<ITeamDirectory>();
        return await teams.NotifyListAsync(teamId, CancellationToken.None);
    }

    // The team has a lead, an agent and an admin; the mode decides who besides the lead hears.
    private async Task<(long Team, long Lead, long Agent, long Admin)> TeamWithThreeMembers(HttpClient admin)
    {
        var team = await CreateTeam(admin);
        var lead = await IdOf(Lead);
        var agent = await IdOf(Agent);
        var adminId = await IdOf(Admin);
        await AddMember(admin, team, lead);
        await AddMember(admin, team, agent);
        await AddMember(admin, team, adminId);
        return (team, lead, agent, adminId);
    }

    [Fact]
    public async Task Leads_only_mode_tells_the_teams_leads_and_nobody_else()
    {
        var admin = await factory.ClientFor(Admin);
        var (team, lead, _, _) = await TeamWithThreeMembers(admin);

        (await NotifyList(team)).Should().Equal(lead);
    }

    [Fact]
    public async Task Leads_and_selected_mode_adds_only_the_selected_members()
    {
        var admin = await factory.ClientFor(Admin);
        var (team, lead, agent, _) = await TeamWithThreeMembers(admin);

        var set = await admin.PutAsJsonAsync($"/api/teams/{team}/notify", new { mode = "LeadsAndSelected", selectedUserIds = new[] { agent } });
        set.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await NotifyList(team)).Should().BeEquivalentTo([lead, agent]);
        (await admin.GetFromJsonAsync<TeamView>($"/api/teams/{team}"))!.Members.Single(m => m.UserId == agent).Notify.Should().BeTrue();
    }

    [Fact]
    public async Task Everyone_mode_tells_every_member()
    {
        var admin = await factory.ClientFor(Admin);
        var (team, lead, agent, adminId) = await TeamWithThreeMembers(admin);

        (await admin.PutAsJsonAsync($"/api/teams/{team}/notify", new { mode = "Everyone" })).EnsureSuccessStatusCode();

        (await NotifyList(team)).Should().BeEquivalentTo([lead, agent, adminId]);
    }

    [Fact]
    public async Task A_disabled_member_is_never_told()
    {
        var admin = await factory.ClientFor(Admin);
        var team = await CreateTeam(admin);
        var email = await factory.CreateUserAsync(Roles.TeamLead);
        var spareLead = await IdOf(email);
        await AddMember(admin, team, spareLead);
        (await NotifyList(team)).Should().Equal(spareLead);

        (await admin.PostAsync($"/api/users/{spareLead}/disable", null)).EnsureSuccessStatusCode();

        (await NotifyList(team)).Should().BeEmpty();
    }

    [Fact]
    public async Task Work_with_no_team_tells_every_active_team_lead()
    {
        var lead = await IdOf(Lead);
        var agent = await IdOf(Agent);

        var list = await NotifyList(null);

        list.Should().Contain(lead);
        list.Should().NotContain(agent);
    }
}
