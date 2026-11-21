using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// A request for help and its whole conversation. Every rule about what may happen to a ticket lives here.
internal sealed class Ticket : AggregateRoot<TicketId>
{
    public const int SubjectMaxLength = 200;
    public const int MessageMaxLength = 10_000;
    public const int ReasonMaxLength = 500;
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromDays(7);

    private readonly List<TicketMessage> _messages = [];

    private Ticket() { }

    public string Subject { get; private set; } = "";
    public TicketPriority Priority { get; private set; }
    public TicketStatus Status { get; private set; }
    public UserId RequesterId { get; private set; }
    public string RequesterName { get; private set; } = "";
    public UserId? AssigneeId { get; private set; }
    public string? AssigneeName { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? FirstResponseUtc { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public string? CancelReason { get; private set; }

    // While the ticket waits on the requester: since when, and whether the automatic reminder has been sent for this wait.
    public DateTime? PendingSinceUtc { get; private set; }
    public DateTime? ReminderSentUtc { get; private set; }

    // Where the ticket belongs. Both are optional: "Not sure" leaves it unrouted for a team lead to sort (decision D4). The names
    // are copies, so a queue can show them without asking Identity.
    public CategoryId? CategoryId { get; private set; }
    public string? CategoryName { get; private set; }
    public TeamId? TeamId { get; private set; }
    public string? TeamName { get; private set; }
    public IReadOnlyList<TicketMessage> Messages => _messages;

    public bool IsFinished => Status is TicketStatus.Closed or TicketStatus.Cancelled;

    // The id comes from the database sequence before the ticket exists, so the events it raises already carry it.
    // The request itself is the conversation's first message, written by the requester.
    public static Ticket Create(TicketId id, string subject, string message, TicketPriority priority, UserId requester, string requesterName,
        DateTime nowUtc, Category? category = null, bool categoryChosen = false)
    {
        EnsureCategoryAvailable(category, categoryChosen);

        var ticket = new Ticket
        {
            Id = id,
            Subject = CheckedSubject(subject),
            Priority = priority,
            Status = TicketStatus.New,
            RequesterId = requester,
            RequesterName = requesterName,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
        ticket._messages.Add(new TicketMessage(requester, requesterName, CheckedMessage(message), isInternal: false, nowUtc));
        if (category is not null)
        {
            ticket.CategoryId = category.Id;
            ticket.CategoryName = category.Name;
            ticket.TeamId = category.TeamId;
            ticket.TeamName = category.TeamName;
        }
        ticket.Raise(new TicketCreatedEvent(Guid.NewGuid(), id, ticket.Subject, ticket.TeamId, requester, priority, nowUtc));
        return ticket;
    }

    // An agent picks up a ticket nobody has. Taking your own ticket again changes nothing.
    public void Take(UserId agent, string agentName, DateTime nowUtc, bool agentIsMember = false, bool agentIsAdmin = false)
    {
        EnsureIn("take", TicketStatus.New, TicketStatus.Open, TicketStatus.Pending);
        EnsureCanOwn(agentIsMember, agentIsAdmin);
        if (AssigneeId is not null && AssigneeId != agent)
            throw new DomainException(TicketsErrors.TakenBySomeoneElse, $"{AssigneeName} already has ticket #{Id.Value}.");
        AssignTo(agent, agentName, assignedBy: agent, nowUtc);
    }

    // A team lead hands a ticket to someone, whoever had it before.
    public void Assign(UserId assignee, string assigneeName, UserId assignedBy, DateTime nowUtc, bool assigneeIsMember = false, bool assigneeIsAdmin = false)
    {
        EnsureIn("assign", TicketStatus.New, TicketStatus.Open, TicketStatus.Pending);
        EnsureCanOwn(assigneeIsMember, assigneeIsAdmin);
        AssignTo(assignee, assigneeName, assignedBy, nowUtc);
    }

    // A team lead changes where a ticket belongs. The assignee stays only if they are in the new team (or are an admin, who
    // may work any team's tickets). Otherwise the ticket loses its owner, and an open or pending one goes back to New:
    // a ticket in progress always has an owner.
    public void Route(Category? category, TeamRef? team, DateTime nowUtc, bool assigneeIsMember = false, bool assigneeIsAdmin = false)
    {
        EnsureIn("route", TicketStatus.New, TicketStatus.Open, TicketStatus.Pending);
        // Keeping the category the ticket already has is always allowed, even if the category has been switched off since.
        var categoryChanged = category is not null && CategoryId != category.Id;
        EnsureCategoryAvailable(category, categoryChosen: categoryChanged);
        if (team is not null && !team.Value.IsActive && TeamId != team.Value.Id)
            throw new DomainException(TicketsErrors.TeamInactive);

        CategoryId = category?.Id;
        CategoryName = category?.Name;
        TeamId = team?.Id;
        TeamName = team?.Name;

        var staysWithAssignee = AssigneeId is null || team is null || assigneeIsMember || assigneeIsAdmin;
        if (!staysWithAssignee)
        {
            AssigneeId = null;
            AssigneeName = null;
            if (Status == TicketStatus.Pending) Raise(new TicketResumedEvent(Id, nowUtc));
            if (Status is TicketStatus.Open or TicketStatus.Pending) Status = TicketStatus.New;
        }
        UpdatedUtc = nowUtc;
    }

    public void Reply(UserId author, string authorName, string body, bool isInternal, bool authorIsStaff, DateTime nowUtc,
        bool authorIsMember = false, bool authorIsAdmin = false)
    {
        EnsureNotFinished();
        var text = CheckedMessage(body);
        if (isInternal && !authorIsStaff)
            throw new DomainException(TicketsErrors.RequesterCannotWriteNotes);

        if (!isInternal && !authorIsStaff)
        {
            // The requester answered: the ball is back with the agents.
            if (Status == TicketStatus.Pending)
            {
                Status = TicketStatus.Open;
                Raise(new TicketResumedEvent(Id, nowUtc));
            }
            else if (Status == TicketStatus.Resolved) Reopen(nowUtc);
        }
        else if (!isInternal)
        {
            // An agent answering a ticket nobody has takes it: an open ticket always has an owner. So the same rule as Take
            // applies, and an agent outside the ticket's team can't get hold of it by replying.
            if (AssigneeId is null) EnsureCanOwn(authorIsMember, authorIsAdmin);
            if (FirstResponseUtc is null)
            {
                FirstResponseUtc = nowUtc;
                Raise(new FirstAgentReplyEvent(Id, nowUtc));
            }
            if (AssigneeId is null) AssignTo(author, authorName, assignedBy: author, nowUtc);
            if (Status == TicketStatus.New) Status = TicketStatus.Open;
        }

        _messages.Add(new TicketMessage(author, authorName, text, isInternal, nowUtc));
        UpdatedUtc = nowUtc;
        Raise(new ReplyAddedEvent(Guid.NewGuid(), Id, Subject, author, authorIsStaff, isInternal, text, RequesterId, AssigneeId, TeamId));
    }

    // Waiting on the requester: an agent asked something and can't go on without the answer.
    public void MarkPending(DateTime nowUtc)
    {
        EnsureIn("wait on the requester", TicketStatus.Open);
        Move(TicketStatus.Pending, nowUtc);
        PendingSinceUtc = nowUtc;
        ReminderSentUtc = null;
        Raise(new TicketPendingEvent(Id, nowUtc));
    }

    // The automatic reminder (decision E4): once for each wait, the requester is asked again. A ticket that is no longer waiting, or
    // that has been reminded, changes nothing.
    public void RemindRequester(DateTime nowUtc)
    {
        if (Status != TicketStatus.Pending) return;
        if (ReminderSentUtc is not null) return;

        ReminderSentUtc = nowUtc;
        Raise(new PendingReminderEvent(Guid.NewGuid(), Id, Subject, RequesterId));
    }

    public void Resolve(DateTime nowUtc)
    {
        EnsureIn("resolve", TicketStatus.Open, TicketStatus.Pending);
        Move(TicketStatus.Resolved, nowUtc);
        ResolvedUtc = nowUtc;
        Raise(new TicketResolvedEvent(Id, nowUtc));
    }

    public void Close(DateTime nowUtc)
    {
        EnsureIn("close", TicketStatus.Resolved);
        Move(TicketStatus.Closed, nowUtc);
        ClosedUtc = nowUtc;
    }

    // A resolved ticket comes back to life for a week, if the fix didn't hold.
    public void Reopen(DateTime nowUtc)
    {
        EnsureIn("reopen", TicketStatus.Resolved);
        if (nowUtc - ResolvedUtc!.Value > ReopenWindow)
            throw new DomainException(TicketsErrors.ReopenWindowPassed,
                $"Ticket #{Id.Value} was resolved on {ResolvedUtc:yyyy-MM-dd}; it can be reopened for 7 days.");
        Move(TicketStatus.Open, nowUtc);
        ResolvedUtc = null;
        Raise(new TicketReopenedEvent(Id, nowUtc));
    }

    public void Cancel(string reason, DateTime nowUtc)
    {
        EnsureIn("cancel", TicketStatus.New, TicketStatus.Open, TicketStatus.Pending);
        var text = reason?.Trim() ?? "";
        if (text.Length is 0 or > ReasonMaxLength)
            throw new DomainException(TicketsErrors.CancelReasonRequired);
        CancelReason = text;
        Move(TicketStatus.Cancelled, nowUtc);
        Raise(new TicketCancelledEvent(Id, nowUtc));
    }

    public void ChangePriority(TicketPriority priority, DateTime nowUtc)
    {
        EnsureNotFinished();
        var changed = Priority != priority;
        Priority = priority;
        UpdatedUtc = nowUtc;
        if (changed) Raise(new TicketPriorityChangedEvent(Id, priority, nowUtc));
    }

    // Requesters see their own tickets; staff who handle tickets see them all.
    public bool IsVisibleTo(UserId user, bool userIsStaff) => userIsStaff || RequesterId == user;

    // On a ticket with a team, only its members (and admins) can own it (decision D7).
    private void EnsureCanOwn(bool isMember, bool isAdmin)
    {
        if (TeamId is null) return;
        if (isMember || isAdmin) return;

        throw new DomainException(TicketsErrors.NotInTeam);
    }

    private static void EnsureCategoryAvailable(Category? category, bool categoryChosen)
    {
        if (!categoryChosen) return;
        if (category is not null && category.IsActive) return;

        throw new DomainException(TicketsErrors.CategoryNotAvailable);
    }

    private void AssignTo(UserId agent, string agentName, UserId assignedBy, DateTime nowUtc)
    {
        // Taking your own ticket again changes nothing, so it tells nobody.
        var changed = AssigneeId != agent;
        AssigneeId = agent;
        AssigneeName = agentName;
        if (Status == TicketStatus.New) Status = TicketStatus.Open;
        UpdatedUtc = nowUtc;
        if (changed) Raise(new TicketAssignedEvent(Guid.NewGuid(), Id, Subject, agent, assignedBy));
    }

    private void Move(TicketStatus to, DateTime nowUtc)
    {
        Status = to;
        UpdatedUtc = nowUtc;
    }

    private void EnsureNotFinished()
    {
        if (IsFinished)
            throw new DomainException(TicketsErrors.TicketFinished, $"Ticket #{Id.Value} is {Status.ToString().ToLowerInvariant()}.");
    }

    private void EnsureIn(string action, params TicketStatus[] allowed)
    {
        EnsureNotFinished();
        if (!allowed.Contains(Status))
            throw new DomainException(TicketsErrors.InvalidTransition,
                $"Can't {action} a ticket that is {Status.ToString().ToLowerInvariant()}; only one that is " +
                $"{string.Join(" or ", allowed.Select(s => s.ToString().ToLowerInvariant()))}.");
    }

    private static string CheckedSubject(string subject)
    {
        var text = subject?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException(TicketsErrors.SubjectRequired);
        if (text.Length > SubjectMaxLength) throw new DomainException(TicketsErrors.SubjectTooLong);
        return text;
    }

    private static string CheckedMessage(string body)
    {
        var text = body?.Trim() ?? "";
        if (text.Length == 0) throw new DomainException(TicketsErrors.MessageRequired);
        if (text.Length > MessageMaxLength) throw new DomainException(TicketsErrors.MessageTooLong);
        return text;
    }
}
