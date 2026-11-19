using Yasda.Shared.BuildingBlocks;

namespace Yasda.Modules.Tickets.Domain;

// Every business rule Tickets can refuse with. Add new codes at the end; never renumber or reuse one.
internal static class TicketsErrors
{
    public static readonly ErrorCode SubjectRequired = new("TICKETS-0001", "A ticket needs a subject",
        "Write one line saying what the problem is, such as \"Printer on the 3rd floor won't print\".");
    public static readonly ErrorCode SubjectTooLong = new("TICKETS-0002", "The subject is too long",
        "Keep the subject to 200 characters; put the details in the message.");
    public static readonly ErrorCode MessageRequired = new("TICKETS-0003", "The message is empty",
        "Write something before sending.");
    public static readonly ErrorCode MessageTooLong = new("TICKETS-0004", "The message is too long",
        "Keep a message to 10,000 characters. Split a long one into two replies.");
    public static readonly ErrorCode TicketFinished = new("TICKETS-0005", "This ticket is closed",
        "A closed or cancelled ticket can't change any more. File a new ticket and mention this one's number.");
    public static readonly ErrorCode InvalidTransition = new("TICKETS-0006", "The ticket can't move to that status from where it is",
        "Reload to see its current status. The message says which statuses allow this step.");
    public static readonly ErrorCode TakenBySomeoneElse = new("TICKETS-0007", "Someone else already has this ticket",
        "Ask them or your team lead to reassign it if you should take it over.");
    public static readonly ErrorCode RequesterCannotWriteNotes = new("TICKETS-0008", "Only staff can write internal notes",
        "Send a normal reply instead. Internal notes are for agents to talk among themselves.");
    public static readonly ErrorCode ReopenWindowPassed = new("TICKETS-0009", "Too late to reopen this ticket",
        "A resolved ticket can be reopened for 7 days. After that, file a new ticket and mention this one's number.");
    public static readonly ErrorCode CancelReasonRequired = new("TICKETS-0010", "A reason is needed to cancel",
        "Say in a few words why the ticket is cancelled, such as \"duplicate of #1042\" (up to 500 characters).");
    public static readonly ErrorCode AssigneeNotStaff = new("TICKETS-0011", "That person can't be assigned tickets",
        "Choose an active agent, team lead or admin from the list.");
}
