using AwesomeAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// Rules checked against hundreds of generated inputs, not only the ones we thought of.
public class TicketPropertyTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);

    // Whatever an agent does, in whatever order, a ticket never ends up in a state the rules forbid: an Open or
    // Pending ticket always has someone assigned, only a resolved or closed one has a resolved date, and nothing
    // changes once it's finished.
    [Property(MaxTest = 300)]
    public Property No_sequence_of_actions_breaks_the_tickets_rules()
    {
        var actions = Gen.Elements("take", "reply", "note", "requester-reply", "pending", "resolve", "close", "reopen", "cancel")
            .ListOf()
            .ToArbitrary();

        return Prop.ForAll(actions, steps =>
        {
            var ticket = Ticket.Create("Printer", "Help", TicketPriority.Normal, new UserId(5), "Rita Cruz", Now);
            foreach (var step in steps)
            {
                var finishedBefore = ticket.IsFinished;
                var statusBefore = ticket.Status;
                try
                {
                    Do(ticket, step);
                }
                catch (DomainException)
                {
                    // A refused step is fine. What matters is the state it leaves behind.
                }

                if (finishedBefore) ticket.Status.Should().Be(statusBefore, "a closed or cancelled ticket never changes");
                if (ticket.Status is TicketStatus.Open or TicketStatus.Pending)
                    ticket.AssigneeId.HasValue.Should().BeTrue($"an {ticket.Status} ticket after '{step}' needs an owner");
                if (ticket.Status is TicketStatus.Resolved or TicketStatus.Closed)
                    ticket.ResolvedUtc.HasValue.Should().BeTrue("a resolved or closed ticket knows when it was resolved");
                if (ticket.Status is TicketStatus.New or TicketStatus.Open or TicketStatus.Pending)
                    ticket.ResolvedUtc.HasValue.Should().BeFalse("a ticket being worked on isn't resolved");
            }
        });
    }

    private static void Do(Ticket ticket, string step)
    {
        var agent = new UserId(3);
        switch (step)
        {
            case "take": ticket.Take(agent, "Alan Reyes", Now); break;
            case "reply": ticket.Reply(agent, "Alan Reyes", "Hi", isInternal: false, authorIsStaff: true, Now); break;
            case "note": ticket.Reply(agent, "Alan Reyes", "Note", isInternal: true, authorIsStaff: true, Now); break;
            case "requester-reply": ticket.Reply(new UserId(5), "Rita Cruz", "Still broken", isInternal: false, authorIsStaff: false, Now); break;
            case "pending": ticket.MarkPending(Now); break;
            case "resolve": ticket.Resolve(Now); break;
            case "close": ticket.Close(Now); break;
            case "reopen": ticket.Reopen(Now); break;
            case "cancel": ticket.Cancel("No longer needed", Now); break;
        }
    }
}
