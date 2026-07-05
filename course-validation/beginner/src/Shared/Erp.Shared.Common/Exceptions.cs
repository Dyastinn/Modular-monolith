namespace Erp.Shared.Common;

// Per Phase 4 §File1 ℹ️ note: put NotFoundException in Erp.Shared.Common.
public sealed class NotFoundException(string message) : Exception(message);

// Phase 5 §5: repository throws this on a lost-update (rowversion mismatch).
public sealed class ConcurrencyException(string message) : Exception(message);
