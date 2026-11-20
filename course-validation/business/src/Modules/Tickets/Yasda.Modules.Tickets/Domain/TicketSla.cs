namespace Yasda.Modules.Tickets.Domain;

// Tickets' copy of one ticket's deadlines, kept so a queue can sort and mark them without asking Sla. It lives in its
// own table: saving it never touches the ticket's row, so it can't bump the ticket's row version and make an agent's
// save fail, and it raises no ticket event. Sla owns the facts; this only remembers the latest answer it was told.
internal sealed class TicketSla
{
    private TicketSla() { }

    public TicketId TicketId { get; private set; }
    public int Version { get; private set; }
    public DateTime ResponseDueUtc { get; private set; }
    public bool? ResponseMet { get; private set; }
    public DateTime ResolveDueUtc { get; private set; }
    public bool? ResolutionMet { get; private set; }
    public DateTime? NextDueUtc { get; private set; }
    public DateTime? AtRiskFromUtc { get; private set; }

    public static TicketSla ForTicket(TicketId ticketId)
    {
        return new TicketSla { TicketId = ticketId };
    }

    // Deliveries can repeat and arrive out of order. Only a newer version is taken, and the answer says whether it was.
    public bool Apply(int version, DateTime responseDueUtc, bool? responseMet, DateTime resolveDueUtc, bool? resolutionMet,
        DateTime? nextDueUtc, DateTime? atRiskFromUtc)
    {
        if (version <= Version) return false;

        Version = version;
        ResponseDueUtc = responseDueUtc;
        ResponseMet = responseMet;
        ResolveDueUtc = resolveDueUtc;
        ResolutionMet = resolutionMet;
        NextDueUtc = nextDueUtc;
        AtRiskFromUtc = atRiskFromUtc;
        return true;
    }
}
