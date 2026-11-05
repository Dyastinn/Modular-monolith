namespace Erp.Shared.BuildingBlocks;

// Mapped to HTTP 404 by GlobalExceptionHandler.
public class NotFoundException(string message) : Exception(message);
