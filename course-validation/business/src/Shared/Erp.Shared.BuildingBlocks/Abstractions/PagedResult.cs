namespace Erp.Shared.Common;

// Standard envelope carrying items + paging metadata (Phase 10 §4).
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
