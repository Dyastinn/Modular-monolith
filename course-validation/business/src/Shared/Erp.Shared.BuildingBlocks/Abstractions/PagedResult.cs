namespace Erp.Shared.BuildingBlocks;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// The body of a 201 Created: the new thing's id. Its address is in the Location header.
public sealed record CreatedId(long Id);
