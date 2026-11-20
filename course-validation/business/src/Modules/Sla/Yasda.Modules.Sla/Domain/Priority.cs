namespace Yasda.Modules.Sla.Domain;

// Sla's own copy of the priorities: its domain depends on nothing but BuildingBlocks. The integration handlers map
// Tickets' public enum to this one with a plain switch.
internal enum Priority { Low, Normal, High, Critical }
