using Dapper;
using Microsoft.Extensions.Configuration;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Sla.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Mail about a deadline, sent when Sla's scheduler announces it. Like the other mail they only queue. Sla sends each warning and each
// breach once per target, and the key says which target (kind:ticket:target:person), so a redelivery queues nothing new.

internal sealed record DeadlineTicket(long Id, string Subject, long? AssigneeId, long? TeamId);

internal static class DeadlineMail
{
    // null when the ticket is gone (a deadline for a ticket that no longer exists is not worth failing the delivery for).
    public static async Task<DeadlineTicket?> FindTicketAsync(IDbConnectionFactory connections, long ticketId, CancellationToken ct)
    {
        await using var conn = connections.Create();
        return await conn.QuerySingleOrDefaultAsync<DeadlineTicket>(new CommandDefinition(
            "SELECT id, subject, assignee_id, team_id FROM tickets.get_ticket(@ticketId, NULL)", new { ticketId }, cancellationToken: ct));
    }

    public static string WhichDeadline(string target)
    {
        if (target == "Response") return "answer";
        return "resolution";
    }
}

// A target is in its last quarter: the person who has the ticket hears, or the team's notify list when nobody has it (decision D9).
internal sealed class EmailOnSlaWarning(IDbConnectionFactory connections, IMailQueue mail, ITeamDirectory teams, IConfiguration config)
    : IIntegrationEventHandler<SlaBreachImminent>
{
    public async Task HandleAsync(SlaBreachImminent integrationEvent, CancellationToken ct)
    {
        var ticket = await DeadlineMail.FindTicketAsync(connections, integrationEvent.TicketId, ct);
        if (ticket is null) return;

        var people = new List<long>();
        if (ticket.AssigneeId is not null) people.Add(ticket.AssigneeId.Value);
        else people.AddRange(await teams.NotifyListAsync(ticket.TeamId, ct));

        var subject = $"Ticket #{ticket.Id} is close to its {DeadlineMail.WhichDeadline(integrationEvent.Target)} deadline: {ticket.Subject}";
        var body = $"The {DeadlineMail.WhichDeadline(integrationEvent.Target)} deadline is {integrationEvent.DueUtc:yyyy-MM-dd HH:mm} UTC.\n\n{ticket.Subject}\n\nOpen it: {TicketMail.LinkTo(config, new TicketId(ticket.Id))}";
        foreach (var person in people)
        {
            await mail.QueueAsync($"sla-warning:{ticket.Id}:{integrationEvent.Target}:{person}", person, subject, body, ct);
        }
    }
}

// A target ran out of time: the person who has the ticket and the team's notify list (which always holds the team leads) hear.
internal sealed class EmailOnSlaBreach(IDbConnectionFactory connections, IMailQueue mail, ITeamDirectory teams, IConfiguration config)
    : IIntegrationEventHandler<SlaBreached>
{
    public async Task HandleAsync(SlaBreached integrationEvent, CancellationToken ct)
    {
        var ticket = await DeadlineMail.FindTicketAsync(connections, integrationEvent.TicketId, ct);
        if (ticket is null) return;

        var people = new List<long>();
        if (ticket.AssigneeId is not null) people.Add(ticket.AssigneeId.Value);
        foreach (var person in await teams.NotifyListAsync(ticket.TeamId, ct))
        {
            if (!people.Contains(person)) people.Add(person);
        }

        var subject = $"Ticket #{ticket.Id} missed its {DeadlineMail.WhichDeadline(integrationEvent.Target)} deadline: {ticket.Subject}";
        var body = $"The {DeadlineMail.WhichDeadline(integrationEvent.Target)} deadline was {integrationEvent.DueUtc:yyyy-MM-dd HH:mm} UTC.\n\n{ticket.Subject}\n\nOpen it: {TicketMail.LinkTo(config, new TicketId(ticket.Id))}";
        foreach (var person in people)
        {
            await mail.QueueAsync($"sla-breached:{ticket.Id}:{integrationEvent.Target}:{person}", person, subject, body, ct);
        }
    }
}
