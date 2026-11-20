namespace Yasda.Modules.Tickets.Domain;

// New → Open ⇄ Pending → Resolved → Closed. Resolved can reopen for a while; Closed and Cancelled are final.
internal enum TicketStatus { New, Open, Pending, Resolved, Closed, Cancelled }

internal enum TicketPriority { Low, Normal, High, Critical }
