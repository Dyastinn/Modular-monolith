using System.Net.Http.Json;
using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Yasda.Modules.Sla.Features;

namespace Yasda.Integration.Tests;

// The breach scheduler: deadlines are rows, and a scanner finds the ones that have come. Each test makes its own ticket, then moves
// that ticket's deadlines into the past in the database (time can't be moved forward), and runs the scanner itself.
[Collection("Integration")]
public class SlaSchedulerTests(CustomWebAppFactory factory)
{
    private const string Requester = "requester@yasda.local", Agent = "agent@yasda.local";

    private sealed record Created(long Id);
    private sealed record Me(long Id);
    private sealed record Ticket(DeadlinesView? Sla);
    private sealed record DeadlinesView(bool Paused, bool? ResponseMet);
    private sealed record Clock(DateTime? ResponseWarnedUtc, DateTime? ResponseBreachedUtc, DateTime? NextCheckUtc);

    private async Task<(long TicketId, long AgentId)> FileTakenTicket()
    {
        var requester = await factory.ClientFor(Requester);
        var agent = await factory.ClientFor(Agent);
        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = $"Scheduler {Guid.NewGuid():N}", message = "Please help.", priority = "High" });
        var id = (await response.Content.ReadFromJsonAsync<Created>())!.Id;
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();
        await Waiting.Eventually(() => agent.GetFromJsonAsync<Ticket>($"/api/tickets/{id}")!, t => t!.Sla is not null);
        var me = await agent.GetFromJsonAsync<Me>("/api/auth/me");
        return (id, me!.Id);
    }

    // Moves the response deadline: the warning point and the due time are given as hours from now (negative is the past).
    private async Task MoveResponseDeadline(long ticketId, double atRiskHours, double dueHours)
    {
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync(
            """
            UPDATE sla.sla_clock
            SET response_at_risk_utc = now() + make_interval(secs => @atRisk), response_due_utc = now() + make_interval(secs => @due),
                next_check_utc = CASE WHEN next_check_utc IS NULL THEN NULL ELSE now() + make_interval(secs => @atRisk) END
            WHERE ticket_id = @ticketId
            """, new { ticketId, atRisk = atRiskHours * 3600, due = dueHours * 3600 });
    }

    private async Task<int> ScanOnce()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SlaScanner>().ScanAsync(CancellationToken.None);
    }

    private async Task<Clock> ClockOf(long ticketId)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.QuerySingleAsync<Clock>(
            "SELECT response_warned_utc, response_breached_utc, next_check_utc FROM sla.sla_clock WHERE ticket_id = @ticketId", new { ticketId });
    }

    private async Task<List<string>> MailKeys(long ticketId, string kind)
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        var keys = await conn.QueryAsync<string>("SELECT key FROM identity.outgoing_mail WHERE key LIKE @pattern ORDER BY key", new { pattern = $"{kind}:{ticketId}:%" });
        return keys.ToList();
    }

    [Fact]
    public async Task A_target_in_its_last_quarter_warns_the_assignee_once()
    {
        var (ticketId, agentId) = await FileTakenTicket();
        await MoveResponseDeadline(ticketId, atRiskHours: -1, dueHours: 1);

        await ScanOnce();
        await ScanOnce();

        var clock = await ClockOf(ticketId);
        clock.ResponseWarnedUtc.Should().NotBeNull();
        clock.ResponseBreachedUtc.Should().BeNull();
        (await MailKeys(ticketId, "sla-warning")).Should().Equal($"sla-warning:{ticketId}:Response:{agentId}");
        (await MailKeys(ticketId, "sla-breached")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_missed_target_is_announced_once_to_the_assignee_and_the_team_leads_however_often_the_scanner_runs()
    {
        var (ticketId, agentId) = await FileTakenTicket();
        await MoveResponseDeadline(ticketId, atRiskHours: -2, dueHours: -1);

        // Two passes after each other, then two at the same moment: the second finds nothing because the clock remembers, and
        // the rows being scanned are locked, so a scanner running alongside skips them.
        await ScanOnce();
        var firstBreach = (await ClockOf(ticketId)).ResponseBreachedUtc;
        await ScanOnce();
        await Task.WhenAll(ScanOnce(), ScanOnce());

        var clock = await ClockOf(ticketId);
        clock.ResponseBreachedUtc.Should().Be(firstBreach, "the breach is recorded once");
        clock.ResponseWarnedUtc.Should().BeNull("a target that is already late is breached, not warned");
        var keys = await MailKeys(ticketId, "sla-breached");
        keys.Should().OnlyHaveUniqueItems();
        keys.Should().Contain($"sla-breached:{ticketId}:Response:{agentId}");
        keys.Count.Should().BeGreaterThan(1, "the team leads hear as well as the assignee");
        (await MailKeys(ticketId, "sla-warning")).Should().BeEmpty();
    }

    // A restart is a new scanner with no memory of the old one: what it knows is in the database.
    [Fact]
    public async Task A_restart_mid_breach_escalates_once()
    {
        var (ticketId, _) = await FileTakenTicket();
        await MoveResponseDeadline(ticketId, atRiskHours: -2, dueHours: -1);
        await ScanOnce();
        var keysBefore = await MailKeys(ticketId, "sla-breached");

        await using (var restarted = factory.WithWebHostBuilder(_ => { }))
        {
            await using var scope = restarted.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<SlaScanner>().ScanAsync(CancellationToken.None);
        }

        (await MailKeys(ticketId, "sla-breached")).Should().Equal(keysBefore);
    }

    [Fact]
    public async Task A_ticket_waiting_on_the_requester_is_never_warned_or_breached()
    {
        var (ticketId, _) = await FileTakenTicket();
        var agent = await factory.ClientFor(Agent);
        (await agent.PostAsync($"/api/tickets/{ticketId}/pending", null)).EnsureSuccessStatusCode();
        await Waiting.Eventually(() => agent.GetFromJsonAsync<Ticket>($"/api/tickets/{ticketId}")!, t => t!.Sla?.Paused == true);
        await MoveResponseDeadline(ticketId, atRiskHours: -2, dueHours: -1);

        await ScanOnce();

        var clock = await ClockOf(ticketId);
        clock.NextCheckUtc.Should().BeNull("a paused clock has nothing to check");
        clock.ResponseBreachedUtc.Should().BeNull();
        (await MailKeys(ticketId, "sla-breached")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_deadline_met_in_time_is_never_announced()
    {
        var (ticketId, _) = await FileTakenTicket();
        var agent = await factory.ClientFor(Agent);
        (await agent.PostAsJsonAsync($"/api/tickets/{ticketId}/replies", new { body = "On it." })).EnsureSuccessStatusCode();
        await Waiting.Eventually(() => agent.GetFromJsonAsync<Ticket>($"/api/tickets/{ticketId}")!, t => t!.Sla?.ResponseMet == true);
        await MoveResponseDeadline(ticketId, atRiskHours: -2, dueHours: -1);

        await ScanOnce();

        (await ClockOf(ticketId)).ResponseBreachedUtc.Should().BeNull("the response target was met, so it is no longer running");
    }
}
