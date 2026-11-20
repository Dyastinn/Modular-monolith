using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Yasda.Modules.Identity.Contracts;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Integration.Tests;

// Tickets queue mail while the outbox delivers an event; Identity's mail sender sends it afterwards. These tests read
// identity.outgoing_mail, the queue itself, after the outbox has delivered. Each builds its own team and ticket, with a
// subject nobody else uses, because all the tests share one database.
[Collection("Integration")]
public class MailTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local", Requester = "requester@yasda.local";

    private sealed record Created(long Id);
    private sealed record Me(long Id, string FullName);
    private sealed record Mail(string Key, string ToAddress, string Subject, DateTime? SentUtc, int Attempts, string? LastError);

    private async Task<long> IdOf(string email)
    {
        var client = await factory.ClientFor(email);
        return (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
    }

    private static async Task<long> MakeTeam(HttpClient admin, params long[] members)
    {
        var response = await admin.PostAsJsonAsync("/api/teams", new { name = $"Team {Guid.NewGuid():N}" });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;
        foreach (var member in members)
            (await admin.PostAsJsonAsync($"/api/teams/{id}/members", new { userId = member })).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<long> MakeCategory(HttpClient lead, long teamId)
    {
        var response = await lead.PostAsJsonAsync("/api/categories", new { name = $"Category {Guid.NewGuid():N}", teamId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<long> FileTicket(HttpClient requester, long? categoryId)
    {
        var response = await requester.PostAsJsonAsync("/api/tickets",
            new { subject = $"Mail {Guid.NewGuid():N}", message = "Please help.", priority = "Normal", categoryId });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task Reply(HttpClient client, long ticketId, string body, bool isInternal = false)
    {
        var response = await client.PostAsJsonAsync($"/api/tickets/{ticketId}/replies", new { body, isInternal });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // The mail queued about one ticket, once the outbox has delivered everything. The kind is how the subject starts:
    // "New ticket #12", "Ticket #12 was assigned", "Re: ticket #12".
    private async Task<List<Mail>> MailAbout(long ticketId, string kind)
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        var rows = await conn.QueryAsync<Mail>(
            """
            SELECT key, to_address AS ToAddress, subject, sent_utc AS SentUtc, attempts, last_error AS LastError
            FROM identity.outgoing_mail
            WHERE subject LIKE @pattern
            """,
            new { pattern = $"{kind} #{ticketId}%" });
        return rows.ToList();
    }

    private static string[] Recipients(List<Mail> mail)
    {
        var addresses = new List<string>();
        foreach (var row in mail) addresses.Add(row.ToAddress);
        addresses.Sort();
        return addresses.ToArray();
    }

    // A team with one lead and one agent that nobody else uses, a category that routes to it, and the two people's addresses.
    private async Task<(long Team, long Category, string LeadEmail, string AgentEmail)> TeamWithCategory(HttpClient admin, HttpClient seededLead)
    {
        var leadEmail = await factory.CreateUserAsync(Roles.TeamLead);
        var agentEmail = await factory.CreateUserAsync(Roles.Agent);
        var team = await MakeTeam(admin, await IdOf(leadEmail), await IdOf(agentEmail));
        var category = await MakeCategory(seededLead, team);
        return (team, category, leadEmail, agentEmail);
    }

    [Fact]
    public async Task An_agent_reply_queues_one_mail_to_the_requester()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket(requester, null);

        await Reply(agent, id, "Have you tried turning it off and on again?");

        var mail = await MailAbout(id, "Re: ticket");
        mail.Should().ContainSingle().Which.ToAddress.Should().Be(Requester);
        mail[0].Subject.Should().Contain("Mail ");
        var sent = await Waiting.Eventually(async () => (await MailAbout(id, "Re: ticket")).Single().SentUtc, when => when is not null);
        sent.Should().NotBeNull("the mail sender sends what is queued");
    }

    [Fact]
    public async Task An_internal_note_queues_no_mail()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket(requester, null);

        await Reply(agent, id, "Fuser again, I bet.", isInternal: true);

        (await MailAbout(id, "Re: ticket")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_ticket_mails_the_teams_leads()
    {
        var admin = await factory.ClientFor(Admin);
        var seededLead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var (_, category, leadEmail, _) = await TeamWithCategory(admin, seededLead);

        var id = await FileTicket(requester, category);

        var mail = await MailAbout(id, "New ticket");
        Recipients(mail).Should().Equal(leadEmail);
    }

    [Fact]
    public async Task A_new_unrouted_ticket_mails_every_team_lead()
    {
        var requester = await factory.ClientFor(Requester);
        var id = await FileTicket(requester, null);

        var mail = await MailAbout(id, "New ticket");

        Recipients(mail).Should().Contain(Lead);
        Recipients(mail).Should().NotContain([Agent, Requester, Admin]);
    }

    [Fact]
    public async Task A_requester_reply_to_an_unassigned_ticket_mails_the_notify_list()
    {
        var admin = await factory.ClientFor(Admin);
        var seededLead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var (team, category, leadEmail, agentEmail) = await TeamWithCategory(admin, seededLead);
        var agentId = await IdOf(agentEmail);
        (await admin.PutAsJsonAsync($"/api/teams/{team}/notify", new { mode = "LeadsAndSelected", selectedUserIds = new[] { agentId } }))
            .EnsureSuccessStatusCode();
        var id = await FileTicket(requester, category);

        await Reply(requester, id, "Any news?");

        var mail = await MailAbout(id, "Re: ticket");
        Recipients(mail).Should().BeEquivalentTo([leadEmail, agentEmail]);
    }

    [Fact]
    public async Task A_requester_reply_to_an_assigned_ticket_mails_only_the_assignee()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket(requester, null);
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        await Reply(requester, id, "Any news?");

        Recipients(await MailAbout(id, "Re: ticket")).Should().Equal(Agent);
    }

    [Fact]
    public async Task Everyone_mode_mails_every_member()
    {
        var admin = await factory.ClientFor(Admin);
        var seededLead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var (team, category, leadEmail, agentEmail) = await TeamWithCategory(admin, seededLead);
        (await admin.PutAsJsonAsync($"/api/teams/{team}/notify", new { mode = "Everyone" })).EnsureSuccessStatusCode();

        var id = await FileTicket(requester, category);

        Recipients(await MailAbout(id, "New ticket")).Should().BeEquivalentTo([leadEmail, agentEmail]);
    }

    [Fact]
    public async Task Taking_a_ticket_yourself_queues_no_mail()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket(requester, null);

        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();

        (await MailAbout(id, "Ticket")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_team_lead_assigning_a_ticket_mails_the_assignee()
    {
        var requester = await factory.ClientFor(Requester);
        var lead = await factory.ClientFor(Lead);
        var id = await FileTicket(requester, null);

        (await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = await IdOf(Agent) })).EnsureSuccessStatusCode();

        Recipients(await MailAbout(id, "Ticket")).Should().Equal(Agent);
    }

    [Fact]
    public async Task Redelivering_a_reply_event_queues_it_once()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket(requester, null);
        await Reply(agent, id, "Looking now.");
        (await MailAbout(id, "Re: ticket")).Should().HaveCount(1);

        // The post office delivers the same event again, as it does after a crash between handling and marking it done.
        await using var conn = factory.OpenDatabase();
        var again = await conn.ExecuteAsync(
            """
            UPDATE tickets.outbox_message SET processed_utc = NULL
            WHERE type LIKE '%ReplyAddedEvent%' AND (payload -> 'TicketId' ->> 'Value')::bigint = @id
            """,
            new { id });
        again.Should().Be(1, "the reply event is in the outbox");
        await Waiting.OutboxesAreDrainedAsync(conn);

        (await MailAbout(id, "Re: ticket")).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_disabled_assignee_gets_no_mail()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);
        var requester = await factory.ClientFor(Requester);
        var spareEmail = await factory.CreateUserAsync(Roles.Agent);
        var spareId = await IdOf(spareEmail);
        var id = await FileTicket(requester, null);
        (await lead.PostAsJsonAsync($"/api/tickets/{id}/assign", new { assigneeId = spareId })).EnsureSuccessStatusCode();
        (await admin.PostAsync($"/api/users/{spareId}/disable", null)).EnsureSuccessStatusCode();

        await Reply(requester, id, "Any news?");

        (await MailAbout(id, "Re: ticket")).Should().BeEmpty();
    }

    [Fact]
    public async Task Smtp_failure_retries_then_parks_the_mail_without_blocking_the_outbox()
    {
        var leadId = await IdOf(Lead);
        await using var scope = factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<IMailQueue>();
        var token = Guid.NewGuid().ToString("N");
        await queue.QueueAsync($"test:{token}:bad", leadId, $"[fail-test] {token}", "This one cannot be delivered.", CancellationToken.None);
        await queue.QueueAsync($"test:{token}:good", leadId, $"Fine {token}", "This one can.", CancellationToken.None);

        // Meanwhile the outbox keeps delivering: a ticket filed now still gets its mail queued.
        var requester = await factory.ClientFor(Requester);
        var id = await FileTicket(requester, null);
        (await MailAbout(id, "New ticket")).Should().NotBeEmpty();

        var bad = await Waiting.Eventually(() => Read(token, "bad"), mail => mail.Attempts >= 3);
        bad.Attempts.Should().Be(3, "the sender stops at Mail:MaxAttempts");
        bad.SentUtc.Should().BeNull();
        bad.LastError.Should().Contain("refused");
        var good = await Waiting.Eventually(() => Read(token, "good"), mail => mail.SentUtc is not null);
        good.SentUtc.Should().NotBeNull("one bad mail never blocks the others");
    }

    private async Task<Mail> Read(string token, string which)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.QuerySingleAsync<Mail>(
            "SELECT key, to_address AS ToAddress, subject, sent_utc AS SentUtc, attempts, last_error AS LastError FROM identity.outgoing_mail WHERE key = @key",
            new { key = $"test:{token}:{which}" });
    }
}
