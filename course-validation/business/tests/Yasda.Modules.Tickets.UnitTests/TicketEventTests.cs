using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// What a ticket tells the rest of the system, and when it stays quiet. Other modules (Sla) rely on each of these.
public class TicketEventTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Requester = new(5), Agent = new(3);
    private static readonly TicketId Id = new(42);

    private static Ticket NewTicket()
    {
        return Ticket.Create(Id, "Printer won't print", "It says paper jam.", TicketPriority.High, Requester, "Rita Cruz", Now);
    }

    private static Ticket OpenTicket()
    {
        var ticket = NewTicket();
        ticket.Take(Agent, "Alan Reyes", Now);
        ticket.ClearEvents();
        return ticket;
    }

    private static List<IDomainEvent> EventsOf(Ticket ticket)
    {
        return ticket.DomainEvents.ToList();
    }

    // The events Sla listens for. The assignment and reply events are for email and have their own tests below.
    private static List<IDomainEvent> SlaEventsOf(Ticket ticket)
    {
        var events = new List<IDomainEvent>();
        foreach (var domainEvent in ticket.DomainEvents)
        {
            if (domainEvent is TicketAssignedEvent or ReplyAddedEvent) continue;
            events.Add(domainEvent);
        }
        return events;
    }

    [Fact]
    public void Creating_a_ticket_raises_one_event_with_its_number_priority_and_time()
    {
        var ticket = NewTicket();

        var raised = EventsOf(ticket);

        var created = raised.Should().ContainSingle().Which.Should().BeOfType<TicketCreatedEvent>().Which;
        created.TicketId.Should().Be(Id);
        created.Subject.Should().Be("Printer won't print");
        created.Priority.Should().Be(TicketPriority.High);
        created.CreatedUtc.Should().Be(Now);
        created.RequesterId.Should().Be(Requester);
        created.TeamId.Should().BeNull();
        created.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void An_agents_first_public_reply_raises_the_first_reply_event_and_a_second_one_does_not()
    {
        var ticket = OpenTicket();

        ticket.Reply(Agent, "Alan Reyes", "Looking now.", isInternal: false, authorIsStaff: true, Now.AddMinutes(5));
        ticket.Reply(Agent, "Alan Reyes", "Still looking.", isInternal: false, authorIsStaff: true, Now.AddMinutes(9));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new FirstAgentReplyEvent(Id, Now.AddMinutes(5)));
    }

    [Fact]
    public void An_internal_note_raises_no_first_reply_event()
    {
        var ticket = OpenTicket();

        ticket.Reply(Agent, "Alan Reyes", "Fuser again, I bet.", isInternal: true, authorIsStaff: true, Now.AddMinutes(5));

        SlaEventsOf(ticket).Should().BeEmpty();
    }

    [Fact]
    public void A_requesters_reply_raises_no_first_reply_event()
    {
        var ticket = OpenTicket();

        ticket.Reply(Requester, "Rita Cruz", "Any news?", isInternal: false, authorIsStaff: false, Now.AddMinutes(5));

        SlaEventsOf(ticket).Should().BeEmpty();
    }

    [Fact]
    public void Resolving_raises_the_resolved_event()
    {
        var ticket = OpenTicket();

        ticket.Resolve(Now.AddHours(2));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketResolvedEvent(Id, Now.AddHours(2)));
    }

    [Fact]
    public void Reopening_raises_the_reopened_event()
    {
        var ticket = OpenTicket();
        ticket.Resolve(Now.AddHours(2));
        ticket.ClearEvents();

        ticket.Reopen(Now.AddHours(3));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketReopenedEvent(Id, Now.AddHours(3)));
    }

    [Fact]
    public void A_requesters_reply_to_a_resolved_ticket_reopens_it_and_raises_the_reopened_event()
    {
        var ticket = OpenTicket();
        ticket.Resolve(Now.AddHours(2));
        ticket.ClearEvents();

        ticket.Reply(Requester, "Rita Cruz", "It broke again.", isInternal: false, authorIsStaff: false, Now.AddHours(3));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketReopenedEvent(Id, Now.AddHours(3)));
    }

    [Fact]
    public void Cancelling_raises_the_cancelled_event()
    {
        var ticket = OpenTicket();

        ticket.Cancel("Fixed itself", Now.AddHours(1));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketCancelledEvent(Id, Now.AddHours(1)));
    }

    [Fact]
    public void Changing_the_priority_raises_the_priority_changed_event()
    {
        var ticket = OpenTicket();

        ticket.ChangePriority(TicketPriority.Critical, Now.AddHours(1));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketPriorityChangedEvent(Id, TicketPriority.Critical, Now.AddHours(1)));
    }

    [Fact]
    public void Changing_to_the_priority_it_already_has_raises_nothing()
    {
        var ticket = OpenTicket();

        ticket.ChangePriority(TicketPriority.High, Now.AddHours(1));

        SlaEventsOf(ticket).Should().BeEmpty();
    }

    [Fact]
    public void Taking_and_closing_raise_nothing_waiting_raises_pending_and_resolving_raises_resolved()
    {
        var ticket = NewTicket();
        ticket.ClearEvents();

        ticket.Take(Agent, "Alan Reyes", Now.AddMinutes(1));
        ticket.MarkPending(Now.AddMinutes(2));
        ticket.Resolve(Now.AddMinutes(3));
        ticket.Close(Now.AddMinutes(4));

        SlaEventsOf(ticket).Should().Equal(new TicketPendingEvent(Id, Now.AddMinutes(2)), new TicketResolvedEvent(Id, Now.AddMinutes(3)));
    }

    [Fact]
    public void Waiting_on_the_requester_raises_pending_and_their_answer_raises_resumed()
    {
        var ticket = OpenTicket();
        ticket.MarkPending(Now.AddMinutes(5));
        ticket.ClearEvents();

        ticket.Reply(Requester, "Rita Cruz", "Here you go.", isInternal: false, authorIsStaff: false, Now.AddMinutes(9));

        SlaEventsOf(ticket).Should().ContainSingle().Which.Should().Be(new TicketResumedEvent(Id, Now.AddMinutes(9)));
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void An_agent_reply_or_a_note_while_waiting_does_not_resume_the_clock()
    {
        var ticket = OpenTicket();
        ticket.MarkPending(Now.AddMinutes(5));
        ticket.ClearEvents();

        ticket.Reply(Agent, "Alan Reyes", "Reminder.", isInternal: false, authorIsStaff: true, Now.AddMinutes(9), authorIsMember: true);
        ticket.Reply(Agent, "Alan Reyes", "Note.", isInternal: true, authorIsStaff: true, Now.AddMinutes(10));

        SlaEventsOf(ticket).Should().NotContain(e => e is TicketResumedEvent);
    }

    [Fact]
    public void Assigning_a_ticket_raises_the_assigned_event_with_who_did_it()
    {
        var ticket = OpenTicket();
        var other = new UserId(9);

        ticket.Assign(other, "Bea Lim", assignedBy: Agent, Now.AddMinutes(1));

        var assigned = ticket.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TicketAssignedEvent>().Which;
        assigned.AssigneeId.Should().Be(other);
        assigned.AssignedById.Should().Be(Agent);
        assigned.Subject.Should().Be("Printer won't print");
        assigned.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Taking_a_ticket_raises_the_assigned_event_with_the_agent_as_both_sides()
    {
        var ticket = NewTicket();
        ticket.ClearEvents();

        ticket.Take(Agent, "Alan Reyes", Now.AddMinutes(1));

        var assigned = ticket.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TicketAssignedEvent>().Which;
        assigned.AssigneeId.Should().Be(Agent);
        assigned.AssignedById.Should().Be(Agent);
    }

    [Fact]
    public void Taking_your_own_ticket_again_raises_no_assigned_event()
    {
        var ticket = OpenTicket();

        ticket.Take(Agent, "Alan Reyes", Now.AddMinutes(1));

        ticket.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void A_reply_raises_the_reply_added_event_with_who_wrote_it_and_who_has_the_ticket()
    {
        var ticket = OpenTicket();

        ticket.Reply(Agent, "Alan Reyes", "Looking now.", isInternal: false, authorIsStaff: true, Now.AddMinutes(5));

        var added = ticket.DomainEvents.OfType<ReplyAddedEvent>().Should().ContainSingle().Which;
        added.AuthorId.Should().Be(Agent);
        added.AuthorIsStaff.Should().BeTrue();
        added.IsInternal.Should().BeFalse();
        added.Body.Should().Be("Looking now.");
        added.RequesterId.Should().Be(Requester);
        added.AssigneeId.Should().Be(Agent);
        added.EventId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void An_internal_note_raises_the_reply_added_event_marked_internal()
    {
        var ticket = OpenTicket();

        ticket.Reply(Agent, "Alan Reyes", "Fuser again, I bet.", isInternal: true, authorIsStaff: true, Now.AddMinutes(5));

        ticket.DomainEvents.OfType<ReplyAddedEvent>().Should().ContainSingle().Which.IsInternal.Should().BeTrue();
    }

    [Fact]
    public void An_agents_reply_to_an_unowned_ticket_takes_it_and_raises_a_self_assignment()
    {
        var ticket = NewTicket();
        ticket.ClearEvents();

        ticket.Reply(Agent, "Alan Reyes", "On it.", isInternal: false, authorIsStaff: true, Now.AddMinutes(5));

        var assigned = ticket.DomainEvents.OfType<TicketAssignedEvent>().Should().ContainSingle().Which;
        assigned.AssigneeId.Should().Be(Agent);
        assigned.AssignedById.Should().Be(Agent);
        ticket.DomainEvents.OfType<ReplyAddedEvent>().Should().ContainSingle().Which.AssigneeId.Should().Be(Agent);
    }

    // Sla may have a different SLA for a category, so a change of category is news to it.
    [Fact]
    public void Routing_a_ticket_to_a_category_raises_a_category_changed_event_and_routing_to_the_same_one_again_raises_none()
    {
        var ticket = OpenTicket();
        var category = Category.Create("Hardware", nameTaken: false, team: null, Now);

        ticket.Route(category, null, Now.AddMinutes(1), assigneeIsMember: true);
        ticket.Route(category, null, Now.AddMinutes(2), assigneeIsMember: true);
        ticket.Route(null, null, Now.AddMinutes(3), assigneeIsMember: true);

        var changes = ticket.DomainEvents.OfType<TicketCategoryChangedEvent>().ToList();
        changes.Should().HaveCount(2);
        changes[0].CategoryId.Should().Be(category.Id.Value);
        changes[1].CategoryId.Should().BeNull("the category was taken off");
    }

    [Fact]
    public void A_ticket_filed_with_a_category_says_which_in_its_created_event()
    {
        var category = Category.Create("Hardware", nameTaken: false, team: null, Now);
        var ticket = Ticket.Create(Id, "Printer won't print", "It says paper jam.", TicketPriority.High, Requester, "Rita Cruz", Now, category, categoryChosen: true);

        ticket.DomainEvents.OfType<TicketCreatedEvent>().Should().ContainSingle().Which.CategoryId.Should().Be(category.Id.Value);
    }
}
