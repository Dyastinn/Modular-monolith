namespace Erp.Shared.BuildingBlocks;

// The answer to a bulk import. All or nothing: with any error, Imported is 0 and nothing was saved. Row counts from 1,
// the way a spreadsheet does (the header is row 0).
public sealed record ImportResult(int Rows, int Imported, IReadOnlyList<ImportError> Errors);

public sealed record ImportError(int Row, string Message, string? Code);

public static class ImportLimits
{
    // Big enough for a distributor's catalog in one file; small enough for one request and one transaction.
    public const int MaxRows = 5_000;
}
