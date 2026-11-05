namespace Erp.Shared.BuildingBlocks;

// Application-level exceptions the global handler maps to HTTP statuses (404 / 403 / 409).
public class NotFoundException(string message) : Exception(message);
public class ForbiddenException(string message) : Exception(message);
public class ConcurrencyException(string message) : Exception(message);
