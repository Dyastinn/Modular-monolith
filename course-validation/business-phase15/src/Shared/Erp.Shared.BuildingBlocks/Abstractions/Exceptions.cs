namespace Erp.Shared.Common;

// Application-level exceptions every module and the host can throw and catch (Phase 11 §6).
// DomainException lives in Erp.Shared.BuildingBlocks (it's domain vocabulary); these are the
// application/transport-shaped ones the global handler maps to HTTP statuses.
public class NotFoundException(string message) : Exception(message);
public class ForbiddenException(string message) : Exception(message);
public class ConcurrencyException(string message) : Exception(message);
