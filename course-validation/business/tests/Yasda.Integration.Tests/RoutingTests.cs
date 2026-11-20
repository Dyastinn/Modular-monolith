using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Integration.Tests;

// Categories decide which team a ticket goes to, and a team's tickets are worked by its members (and admins). Each test builds
// its own team, category and tickets, with names nobody else has, because all the tests share one database.
[Collection("Integration")]
public class RoutingTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local", Requester = "requester@yasda.local";

    private sealed record Created(long Id);
    private sealed record Me(long Id, string FullName);
    private sealed record Category(long Id, string Name, long? TeamId, string? TeamName, bool IsActive);
    private sealed record CategoryPage(List<Category> Items, int TotalCount);
    private sealed record TicketView(long Id, string Status, long? AssigneeId, string? AssigneeName, long? CategoryId, string? CategoryName,
        long? TeamId, string? TeamName);
    private sealed record Row(long Id, string? TeamName, string? CategoryName);
    private sealed record Page(List<Row> Items, int TotalCount);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private async Task<long> IdOf(string email)
    {
        var client = await factory.ClientFor(email);
        return (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
    }

    // A new team with the given people in it.
    private static async Task<long> MakeTeam(HttpClient admin, params long[] members)
    {
        var response = await admin.PostAsJsonAsync("/api/teams", new { name = $"Team {Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;
        foreach (var member in members)
            (await admin.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = member })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<long> MakeCategory(HttpClient lead, long? teamId, string? name = null)
    {
        var response = await lead.PostAsJsonAsync("/api/categories", new { name = name ?? $"Category {Guid.NewGuid():N}", teamId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<long> FileTicket(HttpClient requester, long? categoryId, string? subject = null)
    {
        var response = await requester.PostAsJsonAsync("/api/tickets",
            new { subject = subject ?? $"Routing {Guid.NewGuid():N}", message = "Please help.", priority = "Normal", categoryId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<TicketView> Read(HttpClient client, long id)
    {
        return (await client.GetFromJsonAsync<TicketView>($"/api/tickets/{id}"))!;
    }

    // ---- Categories ----

    [Fact]
    public async Task A_team_lead_makes_a_category_that_routes_to_a_team()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var team = await MakeTeam(admin);
        var name = $"Printers {Guid.NewGuid():N}";

        var id = await MakeCategory(lead, team, name);

        var found = await lead.GetFromJsonAsync<Category>($"/api/categories/{id}");
        found!.Name.Should().Be(name);
        found.TeamId.Should().Be(team);
        found.IsActive.Should().BeTrue();
        found.TeamName.Should().StartWith("Team ");
    }

    [Fact]
    public async Task Only_team_leads_and_admins_manage_categories()
    {
        var agent = await factory.ClientFor(Agent);
        var requester = await factory.ClientFor(Requester);

        foreach (var client in new[] { agent, requester })
        {
            var response = await client.PostAsJsonAsync("/api/categories", new { name = $"Sneaky {Guid.NewGuid():N}" });
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        (await agent.GetAsync("/api/categories/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PutAsJsonAsync("/api/categories/1", new { name = "X", isActive = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_category_name_that_is_taken_is_refused_whatever_the_case()
    {
        var lead = await factory.ClientFor(Lead);
        var name = $"Unique {Guid.NewGuid():N}";
        await MakeCategory(lead, null, name);

        var again = await lead.PostAsJsonAsync("/api/categories", new { name = name.ToUpperInvariant() });

        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(again)).Should().Be("TICKETS-0016");
    }

    [Fact]
    public async Task A_category_for_a_team_that_does_not_exist_or_is_switched_off_is_refused()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var team = await MakeTeam(admin);
        (await admin.PostAsync($"/api/teams/{team}/deactivate", null)).EnsureSuccessStatusCode();

        var unknown = await lead.PostAsJsonAsync("/api/categories", new { name = $"A {Guid.NewGuid():N}", teamId = 999999999 });
        var inactive = await lead.PostAsJsonAsync("/api/categories", new { name = $"B {Guid.NewGuid():N}", teamId = team });

        unknown.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(unknown)).Should().Be("TICKETS-0013");
        inactive.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(inactive)).Should().Be("TICKETS-0014");
    }

    [Fact]
    public async Task A_category_can_be_renamed_moved_and_switched_off()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var first = await MakeTeam(admin);
        var second = await MakeTeam(admin);
        var id = await MakeCategory(lead, first);
        var renamed = $"Renamed {Guid.NewGuid():N}";

        var response = await lead.PutAsJsonAsync($"/api/categories/{id}", new { name = renamed, teamId = second, isActive = false });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var found = await lead.GetFromJsonAsync<Category>($"/api/categories/{id}");
        found.Should().BeEquivalentTo(new { Name = renamed, TeamId = (long?)second, IsActive = false });
    }

    [Fact]
    public async Task A_category_that_does_not_exist_is_404()
    {
        var lead = await factory.ClientFor(Lead);

        (await lead.GetAsync("/api/categories/999999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await lead.PutAsJsonAsync("/api/categories/999999999", new { name = "Nothing", isActive = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Requesters_and_agents_only_see_the_categories_in_use_and_leads_see_all()
    {
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var word = Guid.NewGuid().ToString("N");
        var live = await MakeCategory(lead, null, $"{word} live");
        var off = await MakeCategory(lead, null, $"{word} off");
        (await lead.PutAsJsonAsync($"/api/categories/{off}", new { name = $"{word} off", isActive = false })).EnsureSuccessStatusCode();

        var seenByLead = await lead.GetFromJsonAsync<CategoryPage>($"/api/categories?search={word}");
        var seenByRequester = await requester.GetFromJsonAsync<CategoryPage>($"/api/categories?search={word}");

        seenByLead!.Items.Select(c => c.Id).Should().BeEquivalentTo([live, off]);
        seenByRequester!.Items.Select(c => c.Id).Should().Equal(live);
    }

    [Fact]
    public async Task The_categories_list_sorts_and_pages_through_the_address()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var team = await MakeTeam(admin);
        var word = Guid.NewGuid().ToString("N");
        var withTeam = await MakeCategory(lead, team, $"{word} b");
        var withoutTeam = await MakeCategory(lead, null, $"{word} a");

        var byTeam = await lead.GetFromJsonAsync<CategoryPage>($"/api/categories?search={word}&sort=team");
        var byTeamDescending = await lead.GetFromJsonAsync<CategoryPage>($"/api/categories?search={word}&sort=team&desc=true");

        byTeam!.Items.Select(c => c.Id).Should().Equal(withTeam, withoutTeam);
        byTeamDescending!.Items.Select(c => c.Id).Should().Equal(withTeam, withoutTeam);
        (await lead.GetAsync("/api/categories?sort=notAColumn")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_development_categories_are_seeded_with_their_teams()
    {
        var lead = await factory.ClientFor(Lead);

        var seeded = await lead.GetFromJsonAsync<CategoryPage>("/api/categories?pageSize=100");

        seeded!.Items.Should().Contain(c => c.Name == "Hardware" && c.TeamName == "Service desk");
        seeded.Items.Should().Contain(c => c.Name == "Access" && c.TeamName == "Service desk");
        seeded.Items.Should().Contain(c => c.Name == "Network" && c.TeamName == "Network");
    }

    // ---- Filing ----

    [Fact]
    public async Task A_ticket_filed_with_a_category_goes_to_that_categorys_team()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var team = await MakeTeam(admin);
        var category = await MakeCategory(lead, team, $"Printers {Guid.NewGuid():N}");

        var id = await FileTicket(requester, category);

        var ticket = await Read(lead, id);
        ticket.CategoryId.Should().Be(category);
        ticket.TeamId.Should().Be(team);
        ticket.TeamName.Should().StartWith("Team ");
        var queue = await lead.GetFromJsonAsync<Page>($"/api/tickets?team={team}");
        queue!.Items.Should().ContainSingle().Which.Id.Should().Be(id);
    }

    [Fact]
    public async Task A_ticket_filed_with_no_category_is_unrouted_and_can_be_found_that_way()
    {
        var requester = await factory.ClientFor(Requester);
        var lead = await factory.ClientFor(Lead);

        var id = await FileTicket(requester, categoryId: null);

        var ticket = await Read(lead, id);
        ticket.CategoryId.Should().BeNull();
        ticket.TeamId.Should().BeNull();
        var unrouted = await lead.GetFromJsonAsync<Page>($"/api/tickets?team=none&search={id}");
        unrouted!.Items.Select(t => t.Id).Should().Contain(id);
    }

    [Fact]
    public async Task A_category_that_does_not_exist_or_is_switched_off_cannot_be_chosen()
    {
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var off = await MakeCategory(lead, null);
        (await lead.PutAsJsonAsync($"/api/categories/{off}", new { name = $"Off {Guid.NewGuid():N}", isActive = false })).EnsureSuccessStatusCode();

        foreach (var categoryId in new long[] { 999999999, off })
        {
            var response = await requester.PostAsJsonAsync("/api/tickets",
                new { subject = "Routing", message = "Please help.", priority = "Normal", categoryId });
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CodeOf(response)).Should().Be("TICKETS-0017");
        }
    }

    // ---- Who may take a team's ticket (decision D7) ----

    private async Task<(long Team, long Ticket, long Member, HttpClient MemberClient, HttpClient Outsider)> TeamTicket()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var member = await IdOf(Agent);
        var team = await MakeTeam(admin, member);
        var category = await MakeCategory(lead, team);
        var ticket = await FileTicket(requester, category);
        var outsider = await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));
        return (team, ticket, member, await factory.ClientFor(Agent), outsider);
    }

    [Fact]
    public async Task A_member_takes_their_teams_ticket_and_someone_outside_the_team_cannot()
    {
        var (_, ticket, member, memberClient, outsider) = await TeamTicket();

        var refused = await outsider.PostAsync($"/api/tickets/{ticket}/take", null);
        var taken = await memberClient.PostAsync($"/api/tickets/{ticket}/take", null);

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("TICKETS-0012");
        taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read(memberClient, ticket)).AssigneeId.Should().Be(member);
    }

    [Fact]
    public async Task An_admin_may_take_any_teams_ticket()
    {
        var (_, ticket, _, _, _) = await TeamTicket();
        var admin = await factory.ClientFor(Admin);

        var taken = await admin.PostAsync($"/api/tickets/{ticket}/take", null);

        taken.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_lead_cannot_assign_a_ticket_to_someone_outside_its_team_but_can_to_a_member_or_an_admin()
    {
        var (_, ticket, member, memberClient, outsider) = await TeamTicket();
        var lead = await factory.ClientFor(Lead);
        var outsiderId = (await outsider.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var adminId = await IdOf(Admin);

        var refused = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/assign", new { assigneeId = outsiderId });
        var toAdmin = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/assign", new { assigneeId = adminId });
        var toMember = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/assign", new { assigneeId = member });

        refused.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(refused)).Should().Be("TICKETS-0012");
        toAdmin.StatusCode.Should().Be(HttpStatusCode.NoContent);
        toMember.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read(memberClient, ticket)).AssigneeId.Should().Be(member);
    }

    [Fact]
    public async Task An_agent_outside_the_team_cannot_get_its_ticket_by_replying_but_may_leave_a_note()
    {
        var (_, ticket, _, memberClient, outsider) = await TeamTicket();

        var reply = await outsider.PostAsJsonAsync($"/api/tickets/{ticket}/replies", new { body = "I'll take this one." });
        var note = await outsider.PostAsJsonAsync($"/api/tickets/{ticket}/replies", new { body = "FYI from upstairs.", isInternal = true });

        reply.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodeOf(reply)).Should().Be("TICKETS-0012");
        note.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = await Read(memberClient, ticket);
        after.AssigneeId.Should().BeNull();
        after.Status.Should().Be("New");
    }

    [Fact]
    public async Task A_member_answering_an_unowned_team_ticket_takes_it()
    {
        var (_, ticket, member, memberClient, _) = await TeamTicket();

        var reply = await memberClient.PostAsJsonAsync($"/api/tickets/{ticket}/replies", new { body = "On it." });

        reply.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read(memberClient, ticket)).AssigneeId.Should().Be(member);
    }

    // ---- Routing a ticket ----

    [Fact]
    public async Task A_lead_routes_an_unrouted_ticket_to_a_category_and_team()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var team = await MakeTeam(admin);
        var category = await MakeCategory(lead, team);
        var ticket = await FileTicket(requester, categoryId: null);

        var response = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { categoryId = category, teamId = team });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var routed = await Read(lead, ticket);
        routed.CategoryId.Should().Be(category);
        routed.TeamId.Should().Be(team);
    }

    [Fact]
    public async Task Routing_to_a_team_the_owner_is_not_in_takes_the_ticket_from_them_and_it_goes_back_to_new()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var otherTeam = await MakeTeam(admin, await IdOf(Lead));
        var ticket = await FileTicket(requester, categoryId: null);
        (await agent.PostAsync($"/api/tickets/{ticket}/take", null)).EnsureSuccessStatusCode();
        (await Read(lead, ticket)).Status.Should().Be("Open");

        (await lead.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = otherTeam })).EnsureSuccessStatusCode();

        var after = await Read(lead, ticket);
        after.AssigneeId.Should().BeNull();
        after.Status.Should().Be("New");
        after.TeamId.Should().Be(otherTeam);
    }

    [Fact]
    public async Task Routing_to_a_team_the_owner_is_in_keeps_them_as_owner()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var team = await MakeTeam(admin, await IdOf(Agent));
        var ticket = await FileTicket(requester, categoryId: null);
        (await agent.PostAsync($"/api/tickets/{ticket}/take", null)).EnsureSuccessStatusCode();

        (await lead.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = team })).EnsureSuccessStatusCode();

        var after = await Read(lead, ticket);
        after.AssigneeId.Should().Be(await IdOf(Agent));
        after.Status.Should().Be("Open");
    }

    [Fact]
    public async Task Only_team_leads_and_admins_route_tickets_and_the_team_must_exist_and_be_active()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var agent = await factory.ClientFor(Agent);
        var requester = await factory.ClientFor(Requester);
        var ticket = await FileTicket(requester, categoryId: null);
        var off = await MakeTeam(admin);
        (await admin.PostAsync($"/api/teams/{off}/deactivate", null)).EnsureSuccessStatusCode();

        (await agent.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = off })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await requester.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = off })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unknown = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = 999999999 });
        var inactive = await lead.PostAsJsonAsync($"/api/tickets/{ticket}/route", new { teamId = off });
        var missing = await lead.PostAsJsonAsync("/api/tickets/999999999/route", new { teamId = off });

        (await CodeOf(unknown)).Should().Be("TICKETS-0013");
        (await CodeOf(inactive)).Should().Be("TICKETS-0014");
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- The queue's team filter ----

    [Fact]
    public async Task My_teams_shows_the_tickets_of_every_team_I_am_in_and_none_of_the_others()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var mine = await MakeTeam(admin, await IdOf(Agent));
        var alsoMine = await MakeTeam(admin, await IdOf(Agent));
        var notMine = await MakeTeam(admin);
        var inMine = await FileTicket(requester, await MakeCategory(lead, mine));
        var inAlsoMine = await FileTicket(requester, await MakeCategory(lead, alsoMine));
        var inNotMine = await FileTicket(requester, await MakeCategory(lead, notMine));

        var seen = await agent.GetFromJsonAsync<Page>("/api/tickets?team=mine&pageSize=100");

        var ids = seen!.Items.Select(t => t.Id).ToList();
        ids.Should().Contain([inMine, inAlsoMine]);
        ids.Should().NotContain(inNotMine);
    }

    [Fact]
    public async Task My_teams_for_someone_in_no_team_shows_nothing()
    {
        var loner = await factory.ClientFor(await factory.CreateUserAsync(Roles.Agent));

        var seen = await loner.GetFromJsonAsync<Page>("/api/tickets?team=mine");

        seen!.Items.Should().BeEmpty();
        seen.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task A_team_filter_that_makes_no_sense_is_refused_and_requesters_are_not_affected_by_it()
    {
        var agent = await factory.ClientFor(Agent);
        var requester = await factory.ClientFor(Requester);
        var ticket = await FileTicket(requester, categoryId: null);

        (await agent.GetAsync("/api/tickets?team=abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await agent.GetAsync("/api/tickets?team=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var asRequester = await requester.GetFromJsonAsync<Page>("/api/tickets?team=mine&pageSize=100");
        asRequester!.Items.Select(t => t.Id).Should().Contain(ticket);
    }

    // ---- Renaming a team ----

    [Fact]
    public async Task Renaming_a_team_updates_the_name_copied_onto_its_categories_and_tickets()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var team = await MakeTeam(admin);
        var category = await MakeCategory(lead, team);
        var ticket = await FileTicket(requester, category);
        var renamed = $"Renamed {Guid.NewGuid():N}";

        (await admin.PutAsJsonAsync($"/api/teams/{team}", new { name = renamed })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var seenOnTicket = await Waiting.Eventually(() => Read(lead, ticket), t => t.TeamName == renamed);
        var seenOnCategory = await Waiting.Eventually(
            () => lead.GetFromJsonAsync<Category>($"/api/categories/{category}")!, c => c!.TeamName == renamed);
        seenOnTicket.TeamName.Should().Be(renamed);
        seenOnCategory!.TeamName.Should().Be(renamed);
    }

    [Fact]
    public async Task Two_quick_renames_leave_the_newer_name_on_the_copies()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var team = await MakeTeam(admin);
        var category = await MakeCategory(lead, team);
        var first = $"First {Guid.NewGuid():N}";
        var second = $"Second {Guid.NewGuid():N}";

        (await admin.PutAsJsonAsync($"/api/teams/{team}", new { name = first })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/teams/{team}", new { name = second })).EnsureSuccessStatusCode();

        var seen = await Waiting.Eventually(() => lead.GetFromJsonAsync<Category>($"/api/categories/{category}")!, c => c!.TeamName == second);
        seen!.TeamName.Should().Be(second);
    }
}
