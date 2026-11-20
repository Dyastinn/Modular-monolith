using AwesomeAssertions;
using Yasda.Modules.Tickets.Domain;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.UnitTests;

// Which team a ticket belongs to, and who may take it (decision D7: the team's members, and admins).
public class TicketRoutingTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
    private static readonly UserId Requester = new(5), Member = new(3), Outsider = new(8), Admin = new(1);
    private static readonly TeamRef ServiceDesk = new(new TeamId(1), "Service desk", IsActive: true);
    private static readonly TeamRef Network = new(new TeamId(2), "Network", IsActive: true);
    private static readonly TeamRef Closed = new(new TeamId(3), "Old team", IsActive: false);

    private static ErrorCode CodeOf(Action act)
    {
        var exception = FluentActions.Invoking(act).Should().Throw<DomainException>().Which;
        return exception.Error;
    }

    private static Category Hardware()
    {
        return Category.Create("Hardware", nameTaken: false, ServiceDesk, Now);
    }

    private static Ticket Unrouted()
    {
        return Ticket.Create(new TicketId(1), "Printer won't print", "It says paper jam.", TicketPriority.Normal, Requester, "Rita Cruz", Now);
    }

    private static Ticket ServiceDeskTicket()
    {
        return Ticket.Create(new TicketId(1), "Printer won't print", "It says paper jam.", TicketPriority.Normal, Requester, "Rita Cruz",
            Now, Hardware(), categoryChosen: true);
    }

    // ---- Filing ----

    [Fact]
    public void A_ticket_filed_with_a_category_copies_it_and_its_team()
    {
        var ticket = ServiceDeskTicket();

        ticket.CategoryName.Should().Be("Hardware");
        ticket.TeamId.Should().Be(new TeamId(1));
        ticket.TeamName.Should().Be("Service desk");
    }

    [Fact]
    public void A_ticket_filed_with_no_category_is_unrouted()
    {
        var ticket = Unrouted();

        ticket.CategoryId.Should().BeNull();
        ticket.TeamId.Should().BeNull();
        ticket.TeamName.Should().BeNull();
    }

    [Fact]
    public void A_category_that_does_not_exist_is_refused()
    {
        Action file = () => Ticket.Create(new TicketId(1), "x", "y", TicketPriority.Low, Requester, "Rita Cruz", Now, category: null, categoryChosen: true);

        CodeOf(file).Should().Be(TicketsErrors.CategoryNotAvailable);
    }

    [Fact]
    public void A_category_that_is_switched_off_is_refused()
    {
        var category = Hardware();
        category.Update("Hardware", nameTaken: false, ServiceDesk, isActive: false, Now);

        Action file = () => Ticket.Create(new TicketId(1), "x", "y", TicketPriority.Low, Requester, "Rita Cruz", Now, category, categoryChosen: true);

        CodeOf(file).Should().Be(TicketsErrors.CategoryNotAvailable);
    }

    [Fact]
    public void A_category_with_no_team_files_an_unrouted_ticket()
    {
        var category = Category.Create("Other", nameTaken: false, team: null, Now);

        var ticket = Ticket.Create(new TicketId(1), "x", "y", TicketPriority.Low, Requester, "Rita Cruz", Now, category, categoryChosen: true);

        ticket.CategoryName.Should().Be("Other");
        ticket.TeamId.Should().BeNull();
    }

    // ---- Taking, assigning, replying ----

    [Fact]
    public void A_member_takes_their_teams_ticket()
    {
        var ticket = ServiceDeskTicket();

        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);

        ticket.AssigneeId.Should().Be(Member);
    }

    [Fact]
    public void Someone_outside_the_team_cannot_take_its_ticket()
    {
        var ticket = ServiceDeskTicket();

        Action take = () => ticket.Take(Outsider, "Bea Lim", Now, agentIsMember: false);

        CodeOf(take).Should().Be(TicketsErrors.NotInTeam);
        ticket.AssigneeId.Should().BeNull();
    }

    [Fact]
    public void An_admin_may_take_any_teams_ticket()
    {
        var ticket = ServiceDeskTicket();

        ticket.Take(Admin, "Ada Mendoza", Now, agentIsMember: false, agentIsAdmin: true);

        ticket.AssigneeId.Should().Be(Admin);
    }

    [Fact]
    public void Anyone_may_take_a_ticket_that_has_no_team()
    {
        var ticket = Unrouted();

        ticket.Take(Outsider, "Bea Lim", Now);

        ticket.AssigneeId.Should().Be(Outsider);
    }

    [Fact]
    public void A_ticket_cannot_be_assigned_to_someone_outside_its_team_unless_they_are_an_admin()
    {
        var ticket = ServiceDeskTicket();

        Action assign = () => ticket.Assign(Outsider, "Bea Lim", Admin, Now, assigneeIsMember: false);
        CodeOf(assign).Should().Be(TicketsErrors.NotInTeam);

        ticket.Assign(Admin, "Ada Mendoza", Admin, Now, assigneeIsMember: false, assigneeIsAdmin: true);
        ticket.AssigneeId.Should().Be(Admin);
        ticket.Assign(Member, "Alan Reyes", Admin, Now, assigneeIsMember: true);
        ticket.AssigneeId.Should().Be(Member);
    }

    [Fact]
    public void An_agent_outside_the_team_cannot_get_its_ticket_by_replying_and_nothing_changes()
    {
        var ticket = ServiceDeskTicket();

        Action reply = () => ticket.Reply(Outsider, "Bea Lim", "I'll take it.", isInternal: false, authorIsStaff: true, Now, authorIsMember: false);

        CodeOf(reply).Should().Be(TicketsErrors.NotInTeam);
        ticket.AssigneeId.Should().BeNull();
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.Messages.Should().ContainSingle();
        ticket.FirstResponseUtc.Should().BeNull();
    }

    [Fact]
    public void A_member_or_an_admin_replying_takes_the_unowned_ticket()
    {
        var byMember = ServiceDeskTicket();
        var byAdmin = ServiceDeskTicket();

        byMember.Reply(Member, "Alan Reyes", "On it.", isInternal: false, authorIsStaff: true, Now, authorIsMember: true);
        byAdmin.Reply(Admin, "Ada Mendoza", "On it.", isInternal: false, authorIsStaff: true, Now, authorIsMember: false, authorIsAdmin: true);

        byMember.AssigneeId.Should().Be(Member);
        byAdmin.AssigneeId.Should().Be(Admin);
    }

    [Fact]
    public void An_outsider_may_still_write_an_internal_note_and_reply_to_a_ticket_someone_already_has()
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);

        ticket.Reply(Outsider, "Bea Lim", "FYI: same fault upstairs.", isInternal: true, authorIsStaff: true, Now.AddMinutes(1));
        ticket.Reply(Outsider, "Bea Lim", "Hello from the other team.", isInternal: false, authorIsStaff: true, Now.AddMinutes(2));

        ticket.Messages.Should().HaveCount(3);
        ticket.AssigneeId.Should().Be(Member, "replying doesn't take a ticket someone already has");
    }

    [Fact]
    public void A_requester_replying_is_not_affected_by_teams()
    {
        var ticket = ServiceDeskTicket();

        ticket.Reply(Requester, "Rita Cruz", "Any news?", isInternal: false, authorIsStaff: false, Now.AddMinutes(1));

        ticket.Messages.Should().HaveCount(2);
    }

    // ---- Routing ----

    [Fact]
    public void Routing_changes_the_category_and_the_team()
    {
        var ticket = Unrouted();

        ticket.Route(Hardware(), ServiceDesk, Now.AddHours(1));

        ticket.CategoryName.Should().Be("Hardware");
        ticket.TeamId.Should().Be(new TeamId(1));
        ticket.UpdatedUtc.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Routing_can_choose_a_team_without_a_category_and_can_clear_both()
    {
        var ticket = ServiceDeskTicket();

        ticket.Route(category: null, Network, Now.AddHours(1));
        ticket.CategoryId.Should().BeNull();
        ticket.TeamName.Should().Be("Network");

        ticket.Route(category: null, team: null, Now.AddHours(2));
        ticket.TeamId.Should().BeNull();
        ticket.TeamName.Should().BeNull();
    }

    [Fact]
    public void Routing_keeps_the_assignee_who_is_in_the_new_team()
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);

        ticket.Route(category: null, Network, Now.AddHours(1), assigneeIsMember: true);

        ticket.AssigneeId.Should().Be(Member);
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void Routing_keeps_an_admin_assignee_whatever_the_team()
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Admin, "Ada Mendoza", Now, agentIsMember: false, agentIsAdmin: true);

        ticket.Route(category: null, Network, Now.AddHours(1), assigneeIsMember: false, assigneeIsAdmin: true);

        ticket.AssigneeId.Should().Be(Admin);
    }

    [Theory]
    [InlineData("Open")]
    [InlineData("Pending")]
    public void Routing_drops_an_assignee_outside_the_new_team_and_the_ticket_goes_back_to_new(string status)
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);
        if (status == "Pending") ticket.MarkPending(Now.AddMinutes(5));

        ticket.Route(category: null, Network, Now.AddHours(1), assigneeIsMember: false);

        ticket.AssigneeId.Should().BeNull();
        ticket.AssigneeName.Should().BeNull();
        ticket.Status.Should().Be(TicketStatus.New);
        ticket.TeamName.Should().Be("Network");
    }

    [Fact]
    public void Routing_to_no_team_keeps_the_assignee()
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);

        ticket.Route(category: null, team: null, Now.AddHours(1), assigneeIsMember: false);

        ticket.AssigneeId.Should().Be(Member);
        ticket.Status.Should().Be(TicketStatus.Open);
    }

    [Fact]
    public void Routing_to_an_inactive_team_is_refused_and_changes_nothing()
    {
        var ticket = ServiceDeskTicket();

        Action route = () => ticket.Route(category: null, Closed, Now.AddHours(1));

        CodeOf(route).Should().Be(TicketsErrors.TeamInactive);
        ticket.TeamName.Should().Be("Service desk");
    }

    [Fact]
    public void A_ticket_may_keep_routing_that_points_at_a_team_that_has_since_gone_inactive()
    {
        var oldTeam = new TeamRef(new TeamId(3), "Old team", IsActive: true);
        var category = Category.Create("Legacy", nameTaken: false, oldTeam, Now);
        var ticket = Ticket.Create(new TicketId(1), "x", "y", TicketPriority.Low, Requester, "Rita Cruz", Now, category, categoryChosen: true);

        ticket.Route(category, Closed, Now.AddHours(1));

        ticket.TeamId.Should().Be(new TeamId(3));
    }

    [Fact]
    public void A_category_switched_off_since_can_still_stay_on_a_ticket_that_has_it()
    {
        var category = Hardware();
        var ticket = Ticket.Create(new TicketId(1), "x", "y", TicketPriority.Low, Requester, "Rita Cruz", Now, category, categoryChosen: true);
        category.Update("Hardware", nameTaken: false, ServiceDesk, isActive: false, Now.AddHours(1));

        ticket.Route(category, Network, Now.AddHours(2));

        ticket.CategoryName.Should().Be("Hardware");
        ticket.TeamName.Should().Be("Network");
    }

    [Fact]
    public void A_finished_ticket_cannot_be_routed()
    {
        var ticket = ServiceDeskTicket();
        ticket.Cancel("Fixed itself", Now.AddMinutes(5));

        Action route = () => ticket.Route(category: null, Network, Now.AddHours(1));

        CodeOf(route).Should().Be(TicketsErrors.TicketFinished);
    }

    [Fact]
    public void A_resolved_ticket_cannot_be_routed()
    {
        var ticket = ServiceDeskTicket();
        ticket.Take(Member, "Alan Reyes", Now, agentIsMember: true);
        ticket.Resolve(Now.AddMinutes(5));

        Action route = () => ticket.Route(category: null, Network, Now.AddHours(1));

        CodeOf(route).Should().Be(TicketsErrors.InvalidTransition);
    }
}
