namespace Erp.Shared.BuildingBlocks;

// SO-2026-000042: a prefix people recognize, the year, and a counter that only goes up.
public static class DocumentNumber
{
    public static string Format(string prefix, long sequenceValue, DateTime nowUtc) =>
        $"{prefix}-{nowUtc:yyyy}-{sequenceValue:D6}";
}
