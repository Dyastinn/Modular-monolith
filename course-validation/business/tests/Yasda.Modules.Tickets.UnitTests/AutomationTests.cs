using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// The automatic rules (decision E4): the settings, and the ticket methods the rules call.
public class AutomationTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Admin = new(1), Requester = new(5), Agent = new(3);

    private static AutomationSettings NewSettings()
    {
        return AutomationSettings.Create(Admin, "Dyst", Now);
    }

    private static Ticket PendingTicket()
    {
        var ticket = Ticket.Create(new TicketId(42), "Printer won't print", "It says paper jam.", TicketPriority.High, Requester, "Rita Cruz", Now);
        ticket.Take(Agent, "Alan Reyes", Now);
        ticket.MarkPending(Now.AddHours(1));
        ticket.ClearEvents();
        return ticket;
    }

    [Fact]
    public void A_new_installation_has_every_rule_off()
    {
        var settings = NewSettings();

        settings.CloseResolvedAfterDays.Should().BeNull();
        settings.RemindPendingAfterDays.Should().BeNull();
        settings.ClosePendingAfterDays.Should().BeNull();
    }

    [Fact]
    public void Rules_are_switched_on_with_a_number_of_days_and_the_change_is_remembered()
    {
        var settings = NewSettings();

        settings.Change(7, 7, 14, new UserId(2), "Lena Torres", Now.AddHours(1));

        settings.CloseResolvedAfterDays.Should().Be(7);
        settings.RemindPendingAfterDays.Should().Be(7);
        settings.ClosePendingAfterDays.Should().Be(14);
        settings.UpdatedByName.Should().Be("Lena Torres");
        settings.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Cancelling_must_come_after_the_reminder_when_both_rules_are_on()
    {
        var settings = NewSettings();

        foreach (var (remind, close) in new[] { (7, 7), (7, 3) })
        {
            var act = () => settings.Change(null, remind, close, Admin, "Dyst", Now);
            act.Should().Throw<DomainException>().Which.Error.Should().Be(TicketsErrors.CloseBeforeReminder);
        }

        settings.RemindPendingAfterDays.Should().BeNull("a refused change changes nothing");
    }

    [Fact]
    public void Either_pending_rule_may_be_on_alone_whatever_its_number()
    {
        var settings = NewSettings();

        settings.Change(null, null, 3, Admin, "Dyst", Now);
        settings.ClosePendingAfterDays.Should().Be(3);

        settings.Change(null, 30, null, Admin, "Dyst", Now);
        settings.RemindPendingAfterDays.Should().Be(30);
        settings.ClosePendingAfterDays.Should().BeNull();
    }

    [Fact]
    public void Waiting_on_the_requester_remembers_since_when()
    {
        var ticket = PendingTicket();

        ticket.PendingSinceUtc.Should().Be(Now.AddHours(1));
        ticket.ReminderSentUtc.Should().BeNull();
    }

    [Fact]
    public void The_reminder_is_raised_once_for_a_wait()
    {
        var ticket = PendingTicket();

        ticket.RemindRequester(Now.AddDays(7));
        ticket.RemindRequester(Now.AddDays(8));

        var reminder = ticket.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PendingReminderEvent>().Which;
        reminder.RequesterId.Should().Be(Requester);
        reminder.Subject.Should().Be("Printer won't print");
        ticket.ReminderSentUtc.Should().Be(Now.AddDays(7));
    }

    [Fact]
    public void A_new_wait_can_be_reminded_again()
    {
        var ticket = PendingTicket();
        ticket.RemindRequester(Now.AddDays(7));
        ticket.Reply(Requester, "Rita Cruz", "Sorry, here you go.", isInternal: false, authorIsStaff: false, Now.AddDays(8));
        ticket.MarkPending(Now.AddDays(9));
        ticket.ClearEvents();

        ticket.RemindRequester(Now.AddDays(16));

        ticket.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PendingReminderEvent>();
    }

    [Fact]
    public void A_ticket_nobody_answered_is_cancelled_with_an_automatic_reason_and_the_requester_is_told()
    {
        var ticket = PendingTicket();

        ticket.CancelForSilence(14, Now.AddDays(15));

        ticket.Status.Should().Be(TicketStatus.Cancelled);
        ticket.CancelReason.Should().Be("Cancelled automatically: no answer from the requester for 14 days.");
        ticket.DomainEvents.OfType<TicketCancelledEvent>().Should().ContainSingle("the SLA clock stops like for any cancelled ticket");
        var told = ticket.DomainEvents.OfType<TicketCancelledForSilenceEvent>().Should().ContainSingle().Which;
        told.RequesterId.Should().Be(Requester);
        told.Days.Should().Be(14);
    }

    [Fact]
    public void A_ticket_that_is_not_waiting_is_never_reminded()
    {
        var ticket = PendingTicket();
        ticket.Reply(Requester, "Rita Cruz", "Here you go.", isInternal: false, authorIsStaff: false, Now.AddDays(1));
        ticket.ClearEvents();

        ticket.RemindRequester(Now.AddDays(7));

        ticket.DomainEvents.Should().BeEmpty();
        ticket.ReminderSentUtc.Should().BeNull();
    }
}
