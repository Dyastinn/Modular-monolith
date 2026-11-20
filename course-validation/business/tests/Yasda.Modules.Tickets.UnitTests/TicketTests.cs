using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// The ticket's rules, with no database and no web server: each test builds a ticket in memory and pokes it.
public class TicketTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Requester = new(5), Agent = new(3), OtherAgent = new(8);

    private static Ticket NewTicket() =>
        Ticket.Create(new TicketId(1), "Printer won't print", "It says paper jam.", TicketPriority.Normal, Requester, "Rita Cruz", Now);

    private static Ticket OpenTicket()
    {
        var ticket = NewTicket();
        ticket.Take(Agent, "Alan Reyes", Now);
        return ticket;
    }

    private static Ticket ResolvedTicket()
    {
        var ticket = OpenTicket();
        ticket.Resolve(Now);
        return ticket;
    }

    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    // ---- Filing ----

    [Fact]
    public void A_new_ticket_starts_with_the_request_as_its_first_message()
    {
        var ticket = NewTicket();

        ticket.Status.Should().Be(TicketStatus.New);
        ticket.AssigneeId.Should().BeNull();
        ticket.Messages.Should().ContainSingle();
        ticket.Messages[0].Body.Should().Be("It says paper jam.");
        ticket.Messages[0].IsInternal.Should().BeFalse();
    }

    [Fact]
    public void The_subject_is_trimmed() =>
        Ticket.Create(new TicketId(1), "  Printer  ", "Help", TicketPriority.Low, Requester, "Rita Cruz", Now).Subject.Should().Be("Printer");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_ticket_needs_a_subject(string subject) =>
        CodeOf(() => Ticket.Create(new TicketId(1), subject, "Help", TicketPriority.Low, Requester, "Rita Cruz", Now)).Should().Be(TicketsErrors.SubjectRequired);

    [Fact]
    public void A_subject_of_exactly_200_characters_is_fine_and_201_is_too_long()
    {
        Ticket.Create(new TicketId(1), new string('a', 200), "Help", TicketPriority.Low, Requester, "Rita Cruz", Now).Subject.Should().HaveLength(200);
        CodeOf(() => Ticket.Create(new TicketId(1), new string('a', 201), "Help", TicketPriority.Low, Requester, "Rita Cruz", Now))
            .Should().Be(TicketsErrors.SubjectTooLong);
    }

    [Fact]
    public void A_ticket_needs_a_message() =>
        CodeOf(() => Ticket.Create(new TicketId(1), "Printer", " ", TicketPriority.Low, Requester, "Rita Cruz", Now)).Should().Be(TicketsErrors.MessageRequired);

    [Fact]
    public void A_message_over_10000_characters_is_refused() =>
        CodeOf(() => Ticket.Create(new TicketId(1), "Printer", new string('a', 10_001), TicketPriority.Low, Requester, "Rita Cruz", Now))
            .Should().Be(TicketsErrors.MessageTooLong);

    // ---- Taking and assigning ----

    [Fact]
    public void Taking_a_new_ticket_assigns_it_and_opens_it()
    {
        var ticket = OpenTicket();

        ticket.AssigneeId.Should().Be(Agent);
        ticket.AssigneeName.Should().Be("Alan Reyes");
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void Taking_your_own_ticket_again_changes_nothing()
    {
        var ticket = OpenTicket();

        ticket.Take(Agent, "Alan Reyes", Now);

        ticket.AssigneeId.Should().Be(Agent);
    }

    [Fact]
    public void Nobody_takes_a_ticket_someone_else_has() =>
        CodeOf(() => OpenTicket().Take(OtherAgent, "Bea Lim", Now)).Should().Be(TicketsErrors.TakenBySomeoneElse);

    [Fact]
    public void A_team_lead_can_reassign_a_ticket_someone_has()
    {
        var ticket = OpenTicket();

        ticket.Assign(OtherAgent, "Bea Lim", assignedBy: Agent, Now);

        ticket.AssigneeId.Should().Be(OtherAgent);
    }

    [Fact]
    public void A_resolved_ticket_cant_be_taken() =>
        CodeOf(() => ResolvedTicket().Take(OtherAgent, "Bea Lim", Now)).Should().Be(TicketsErrors.InvalidTransition);

    // ---- The conversation ----

    [Fact]
    public void An_agents_first_public_reply_records_the_first_response_time()
    {
        var ticket = NewTicket();

        ticket.Reply(Agent, "Alan Reyes", "Looking now.", isInternal: false, authorIsStaff: true, Now.AddMinutes(10));
        ticket.Reply(Agent, "Alan Reyes", "Fixed?", isInternal: false, authorIsStaff: true, Now.AddMinutes(30));

        ticket.FirstResponseUtc.Should().Be(Now.AddMinutes(10));
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    // Found by the property test below: without this, a reply opened the ticket with nobody responsible for it.
    [Fact]
    public void An_agent_answering_an_unassigned_ticket_takes_it()
    {
        var ticket = NewTicket();

        ticket.Reply(Agent, "Alan Reyes", "Looking now.", isInternal: false, authorIsStaff: true, Now);

        ticket.AssigneeId.Should().Be(Agent);
        ticket.AssigneeName.Should().Be("Alan Reyes");
    }

    [Fact]
    public void A_new_ticket_cant_be_resolved_before_anyone_works_on_it() =>
        CodeOf(() => NewTicket().Resolve(Now)).Should().Be(TicketsErrors.InvalidTransition);

    [Fact]
    public void An_internal_note_is_not_a_response_to_the_requester()
    {
        var ticket = NewTicket();

        ticket.Reply(Agent, "Alan Reyes", "Probably the fuser.", isInternal: true, authorIsStaff: true, Now);

        ticket.FirstResponseUtc.Should().BeNull();
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Messages[^1].IsInternal.Should().BeTrue();
    }

    [Fact]
    public void A_requester_cant_write_internal_notes() =>
        CodeOf(() => NewTicket().Reply(Requester, "Rita Cruz", "Psst", isInternal: true, authorIsStaff: false, Now))
            .Should().Be(TicketsErrors.RequesterCannotWriteNotes);

    [Fact]
    public void The_requesters_answer_takes_a_pending_ticket_back_to_open()
    {
        var ticket = OpenTicket();
        ticket.MarkPending(Now);

        ticket.Reply(Requester, "Rita Cruz", "It's the company laptop.", isInternal: false, authorIsStaff: false, Now);

        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void An_agents_reply_leaves_a_pending_ticket_pending()
    {
        var ticket = OpenTicket();
        ticket.MarkPending(Now);

        ticket.Reply(Agent, "Alan Reyes", "Any news?", isInternal: false, authorIsStaff: true, Now);

        ticket.Status.Should().Be(TicketStatus.Pending);
    }

    [Fact]
    public void The_requesters_reply_reopens_a_resolved_ticket_within_the_week()
    {
        var ticket = ResolvedTicket();

        ticket.Reply(Requester, "Rita Cruz", "It's jammed again.", isInternal: false, authorIsStaff: false, Now.AddDays(7));

        ticket.Status.Should().Be(TicketStatus.Open);
        ticket.ResolvedUtc.Should().BeNull();
    }

    [Fact]
    public void A_reply_to_a_closed_ticket_is_refused()
    {
        var ticket = ResolvedTicket();
        ticket.Close(Now);

        CodeOf(() => ticket.Reply(Requester, "Rita Cruz", "Hello?", isInternal: false, authorIsStaff: false, Now))
            .Should().Be(TicketsErrors.TicketFinished);
    }

    // ---- The ticket's life ----

    [Fact]
    public void Reopening_works_up_to_exactly_seven_days_after_resolving()
    {
        var ticket = ResolvedTicket();

        ticket.Reopen(Now.Add(Ticket.ReopenWindow));

        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void Reopening_a_moment_after_seven_days_is_too_late() =>
        CodeOf(() => ResolvedTicket().Reopen(Now.Add(Ticket.ReopenWindow).AddTicks(1))).Should().Be(TicketsErrors.ReopenWindowPassed);

    // The status is passed by name: a test method is public, and TicketStatus is internal to the module.
    [Theory]
    [InlineData("New")]
    [InlineData("Open")]
    [InlineData("Pending")]
    public void Only_a_resolved_ticket_can_be_closed(string status) =>
        CodeOf(() => TicketIn(Enum.Parse<TicketStatus>(status)).Close(Now)).Should().Be(TicketsErrors.InvalidTransition);

    [Fact]
    public void Only_an_open_ticket_can_wait_on_the_requester() =>
        CodeOf(() => NewTicket().MarkPending(Now)).Should().Be(TicketsErrors.InvalidTransition);

    [Fact]
    public void Cancelling_needs_a_reason_and_keeps_it()
    {
        var ticket = NewTicket();

        CodeOf(() => ticket.Cancel("  ", Now)).Should().Be(TicketsErrors.CancelReasonRequired);
        ticket.Cancel(" Duplicate of #1042 ", Now);

        ticket.Status.Should().Be(TicketStatus.Cancelled);
        ticket.CancelReason.Should().Be("Duplicate of #1042");
    }

    [Fact]
    public void A_cancelled_ticket_changes_no_more()
    {
        var ticket = NewTicket();
        ticket.Cancel("Duplicate", Now);

        CodeOf(() => ticket.ChangePriority(TicketPriority.Critical, Now)).Should().Be(TicketsErrors.TicketFinished);
        CodeOf(() => ticket.Take(Agent, "Alan Reyes", Now)).Should().Be(TicketsErrors.TicketFinished);
    }

    // ---- Who sees what ----

    [Fact]
    public void A_requester_sees_only_their_own_ticket_and_staff_see_all()
    {
        var ticket = NewTicket();

        ticket.IsVisibleTo(Requester, userIsStaff: false).Should().BeTrue();
        ticket.IsVisibleTo(new UserId(99), userIsStaff: false).Should().BeFalse();
        ticket.IsVisibleTo(new UserId(99), userIsStaff: true).Should().BeTrue();
    }

    private static Ticket TicketIn(TicketStatus status)
    {
        var ticket = NewTicket();
        if (status == TicketStatus.New) return ticket;
        ticket.Take(Agent, "Alan Reyes", Now);
        if (status == TicketStatus.Pending) ticket.MarkPending(Now);
        return ticket;
    }
}
