using Yasda.Modules.Sla.Domain;

namespace Yasda.Modules.Sla.Features;

// Tickets' public priority enum to Sla's own. Sla's domain never references a Contracts type.
internal static class PriorityMapping
{
    public static Priority From(Tickets.Contracts.TicketPriority priority)
    {
        switch (priority)
        {
            case Tickets.Contracts.TicketPriority.Low: return Priority.Low;
            case Tickets.Contracts.TicketPriority.Normal: return Priority.Normal;
            case Tickets.Contracts.TicketPriority.High: return Priority.High;
            case Tickets.Contracts.TicketPriority.Critical: return Priority.Critical;
            default: throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown priority.");
        }
    }
}
