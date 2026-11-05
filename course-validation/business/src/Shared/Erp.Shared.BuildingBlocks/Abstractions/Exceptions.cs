namespace Erp.Shared.BuildingBlocks;

// Mapped to HTTP statuses by GlobalExceptionHandler.
public class NotFoundException(string message) : Exception(message);
public class ForbiddenException(string message) : Exception(message);
public class ConcurrencyException(string message) : Exception(message);
