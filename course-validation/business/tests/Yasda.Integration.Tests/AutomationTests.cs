using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Yasda.Modules.Tickets.Features;

namespace Yasda.Integration.Tests;

// The automatic rules (decision E4): nothing is automatic until an Admin switches a rule on. The tests move a ticket's dates into the past in
// the database (time can't be moved forward), run the rules themselves, and always switch every rule off again, because all tests share
// one database.
[Collection("Integration")]
public class AutomationTests(CustomWebAppFactory factory)
{
    private const string Admin = "admin@yasda.local", Lead = "lead@yasda.local", Agent = "agent@yasda.local", Requester = "requester@yasda.local";

    private sealed record Created(long Id);
    private sealed record Settings(int? CloseResolvedAfterDays, int? RemindPendingAfterDays, int? ClosePendingAfterDays, string UpdatedByName);
    private sealed record Ticket(string Status, string? CancelReason);

    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString();
    }

    private async Task SetRules(int? closeResolved, int? remind, int? closePending)
    {
        var admin = await factory.ClientFor(Admin);
        var response = await admin.PutAsJsonAsync("/api/automation", new { closeResolvedAfterDays = closeResolved, remindPendingAfterDays = remind, closePendingAfterDays = closePending });
        response.EnsureSuccessStatusCode();
    }

    private async Task<AutomationResult> RunRules()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<TicketAutomationRunner>().RunAsync(CancellationToken.None);
    }

    private async Task<long> FileTicket(string subjectStart)
    {
        var requester = await factory.ClientFor(Requester);
        var response = await requester.PostAsJsonAsync("/api/tickets", new { subject = $"{subjectStart} {Guid.NewGuid():N}", message = "Please help.", priority = "Low" });
        return (await response.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private async Task<Ticket> Read(long id)
    {
        var agent = await factory.ClientFor(Agent);
        return (await agent.GetFromJsonAsync<Ticket>($"/api/tickets/{id}"))!;
    }

    private async Task BackdateAsync(long id, string column, int days)
    {
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync($"UPDATE tickets.ticket SET {column} = now() - make_interval(days => @days) WHERE id = @id", new { id, days });
    }

    private async Task<long> ResolvedTicket(int resolvedDaysAgo)
    {
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket("Auto close");
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{id}/resolve", null)).EnsureSuccessStatusCode();
        await BackdateAsync(id, "resolved_utc", resolvedDaysAgo);
        return id;
    }

    private async Task<long> PendingTicket(int pendingDaysAgo)
    {
        var agent = await factory.ClientFor(Agent);
        var id = await FileTicket("Auto remind");
        (await agent.PostAsync($"/api/tickets/{id}/take", null)).EnsureSuccessStatusCode();
        (await agent.PostAsync($"/api/tickets/{id}/pending", null)).EnsureSuccessStatusCode();
        await BackdateAsync(id, "pending_since_utc", pendingDaysAgo);
        return id;
    }

    private async Task<int> RemindersAbout(long ticketId)
    {
        await using var conn = factory.OpenDatabase();
        await Waiting.OutboxesAreDrainedAsync(conn);
        return await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM identity.outgoing_mail WHERE subject LIKE @pattern", new { pattern = $"We are still waiting for your answer on ticket #{ticketId}:%" });
    }

    [Fact]
    public async Task Every_rule_starts_off_and_only_an_admin_sees_or_changes_them()
    {
        var admin = await factory.ClientFor(Admin);
        var lead = await factory.ClientFor(Lead);

        var settings = await admin.GetFromJsonAsync<Settings>("/api/automation");

        settings!.CloseResolvedAfterDays.Should().BeNull();
        settings.RemindPendingAfterDays.Should().BeNull();
        settings.ClosePendingAfterDays.Should().BeNull();
        (await lead.GetAsync("/api/automation")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await lead.PutAsJsonAsync("/api/automation", new { closeResolvedAfterDays = 7 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_admin_switches_rules_on_and_off_and_nonsense_is_refused()
    {
        var admin = await factory.ClientFor(Admin);
        try
        {
            await SetRules(7, 7, 14);
            var on = await admin.GetFromJsonAsync<Settings>("/api/automation");
            on.Should().Match<Settings>(s => s.CloseResolvedAfterDays == 7 && s.RemindPendingAfterDays == 7 && s.ClosePendingAfterDays == 14);
            on!.UpdatedByName.Should().NotBe("Installation default");

            var tooMany = await admin.PutAsJsonAsync("/api/automation", new { closeResolvedAfterDays = 400 });
            var zero = await admin.PutAsJsonAsync("/api/automation", new { remindPendingAfterDays = 0 });
            var backwards = await admin.PutAsJsonAsync("/api/automation", new { remindPendingAfterDays = 10, closePendingAfterDays = 5 });

            tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            zero.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            backwards.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await CodeOf(backwards)).Should().Be("TICKETS-0018");
            (await admin.GetFromJsonAsync<Settings>("/api/automation"))!.ClosePendingAfterDays.Should().Be(14, "a refused change changes nothing");
        }
        finally
        {
            await SetRules(null, null, null);
        }
    }

    [Fact]
    public async Task With_every_rule_off_nothing_happens()
    {
        var resolved = await ResolvedTicket(resolvedDaysAgo: 30);
        var pending = await PendingTicket(pendingDaysAgo: 30);

        var result = await RunRules();

        result.Should().Be(new AutomationResult(0, 0, 0));
        (await Read(resolved)).Status.Should().Be("Resolved");
        (await Read(pending)).Status.Should().Be("Pending");
    }

    [Fact]
    public async Task A_resolved_ticket_is_closed_after_the_chosen_days_and_a_newer_one_is_left_alone()
    {
        var old = await ResolvedTicket(resolvedDaysAgo: 10);
        var recent = await ResolvedTicket(resolvedDaysAgo: 2);
        try
        {
            await SetRules(7, null, null);

            var result = await RunRules();

            result.Closed.Should().BeGreaterThanOrEqualTo(1);
            (await Read(old)).Status.Should().Be("Closed");
            (await Read(recent)).Status.Should().Be("Resolved");
        }
        finally
        {
            await SetRules(null, null, null);
        }
    }

    [Fact]
    public async Task A_silent_requester_is_reminded_once_and_then_the_ticket_is_cancelled()
    {
        var silent = await PendingTicket(pendingDaysAgo: 8);
        var fresh = await PendingTicket(pendingDaysAgo: 1);
        try
        {
            await SetRules(null, 7, 14);

            await RunRules();
            await RunRules();

            (await RemindersAbout(silent)).Should().Be(1, "one reminder for one wait, however often the rules run");
            (await RemindersAbout(fresh)).Should().Be(0);
            (await Read(silent)).Status.Should().Be("Pending");

            await BackdateAsync(silent, "pending_since_utc", 15);
            await RunRules();

            var cancelled = await Read(silent);
            cancelled.Status.Should().Be("Cancelled");
            cancelled.CancelReason.Should().Contain("no answer from the requester for 14 days");
            (await Read(fresh)).Status.Should().Be("Pending");
        }
        finally
        {
            await SetRules(null, null, null);
        }
    }

    [Fact]
    public async Task A_requester_who_answers_in_time_is_not_reminded()
    {
        var id = await PendingTicket(pendingDaysAgo: 8);
        var requester = await factory.ClientFor(Requester);
        (await requester.PostAsJsonAsync($"/api/tickets/{id}/replies", new { body = "Sorry, here is the answer." })).EnsureSuccessStatusCode();
        try
        {
            await SetRules(null, 7, 14);

            await RunRules();

            (await RemindersAbout(id)).Should().Be(0);
            (await Read(id)).Status.Should().Be("Open");
        }
        finally
        {
            await SetRules(null, null, null);
        }
    }
}
