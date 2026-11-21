using Microsoft.Extensions.Configuration;
using Yasda.Modules.Identity.Contracts;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Features;

// Mail about tickets. These run in the outbox delivery, so they only queue: Identity's background service sends, and
// a down mail server never holds back the events Sla is waiting for. The key of each mail is "kind:eventId:userId",
// so a redelivered event queues each mail once. Nobody gets mail about their own action.

internal static class TicketMail
{
    // Where the ticket can be opened. App:PublicUrl is an installation setting (the address people reach YASDA at).
    public static string LinkTo(IConfiguration config, TicketId ticketId)
    {
        var baseUrl = (config["App:PublicUrl"] ?? "").TrimEnd('/');
        return $"{baseUrl}/tickets/{ticketId.Value}";
    }
}

internal sealed class EmailOnNewTicket(IMailQueue mail, ITeamDirectory teams, IConfiguration config) : IDomainEventHandler<TicketCreatedEvent>
{
    public async Task HandleAsync(TicketCreatedEvent domainEvent, CancellationToken ct)
    {
        var teamId = domainEvent.TeamId?.Value;
        var people = await teams.NotifyListAsync(teamId, ct);
        var subject = $"New ticket #{domainEvent.TicketId.Value}: {domainEvent.Subject}";
        var body = $"A new ticket needs an owner.\n\n{domainEvent.Subject}\n\nOpen it: {TicketMail.LinkTo(config, domainEvent.TicketId)}";

        foreach (var person in people)
        {
            if (person == domainEvent.RequesterId.Value) continue;
            await mail.QueueAsync($"new-ticket:{domainEvent.EventId}:{person}", person, subject, body, ct);
        }
    }
}

internal sealed class EmailOnAssignment(IMailQueue mail, IConfiguration config) : IDomainEventHandler<TicketAssignedEvent>
{
    public async Task HandleAsync(TicketAssignedEvent domainEvent, CancellationToken ct)
    {
        // Taking a ticket yourself is not news to you.
        if (domainEvent.AssigneeId == domainEvent.AssignedById) return;

        var assignee = domainEvent.AssigneeId.Value;
        var subject = $"Ticket #{domainEvent.TicketId.Value} was assigned to you: {domainEvent.Subject}";
        var body = $"{domainEvent.Subject}\n\nOpen it: {TicketMail.LinkTo(config, domainEvent.TicketId)}";
        await mail.QueueAsync($"assigned:{domainEvent.EventId}:{assignee}", assignee, subject, body, ct);
    }
}

internal sealed class EmailOnReply(IMailQueue mail, ITeamDirectory teams, IConfiguration config) : IDomainEventHandler<ReplyAddedEvent>
{
    public async Task HandleAsync(ReplyAddedEvent domainEvent, CancellationToken ct)
    {
        // Internal notes are for staff to read on the ticket; they are never mailed.
        if (domainEvent.IsInternal) return;

        var people = new List<long>();
        if (domainEvent.AuthorIsStaff)
        {
            people.Add(domainEvent.RequesterId.Value);
        }
        else if (domainEvent.AssigneeId is not null)
        {
            people.Add(domainEvent.AssigneeId.Value.Value);
        }
        else
        {
            // Nobody has the ticket yet, so the people who hear about work nobody has (decision D9).
            var teamId = domainEvent.TeamId?.Value;
            people.AddRange(await teams.NotifyListAsync(teamId, ct));
        }

        var subject = $"Re: ticket #{domainEvent.TicketId.Value}: {domainEvent.Subject}";
        var body = $"{domainEvent.Body}\n\nOpen the ticket: {TicketMail.LinkTo(config, domainEvent.TicketId)}";
        foreach (var person in people)
        {
            if (person == domainEvent.AuthorId.Value) continue;
            await mail.QueueAsync($"reply:{domainEvent.EventId}:{person}", person, subject, body, ct);
        }
    }
}

// The automatic reminder: the requester is asked again to answer a ticket that has waited on them.
internal sealed class EmailOnPendingReminder(IMailQueue mail, IConfiguration config) : IDomainEventHandler<PendingReminderEvent>
{
    public async Task HandleAsync(PendingReminderEvent domainEvent, CancellationToken ct)
    {
        var requester = domainEvent.RequesterId.Value;
        var subject = $"We are still waiting for your answer on ticket #{domainEvent.TicketId.Value}: {domainEvent.Subject}";
        var body = $"We asked you something and can't go on without your answer.\n\n{domainEvent.Subject}\n\nOpen the ticket and reply: {TicketMail.LinkTo(config, domainEvent.TicketId)}";
        await mail.QueueAsync($"pending-reminder:{domainEvent.EventId}:{requester}", requester, subject, body, ct);
    }
}
