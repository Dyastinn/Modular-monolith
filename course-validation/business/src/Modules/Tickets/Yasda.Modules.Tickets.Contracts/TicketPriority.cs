namespace Yasda.Modules.Tickets.Contracts;

// The priorities Tickets tells other modules about. Tickets' own domain keeps an internal enum with the same names, so
// its domain depends on nothing but BuildingBlocks; the publishing handlers map one to the other.
public enum TicketPriority { Low, Normal, High, Critical }
