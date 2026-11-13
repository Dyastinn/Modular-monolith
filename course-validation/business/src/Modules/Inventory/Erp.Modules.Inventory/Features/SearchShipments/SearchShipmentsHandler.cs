using Dapper;
using Erp.Shared.BuildingBlocks;
using FluentValidation;

namespace Erp.Modules.Inventory.Features;

internal sealed record SearchShipmentsQuery(long? SalesOrderId = null, int Page = 1, int PageSize = 20);

internal sealed record ShipmentDto(long Id, string Number, long SalesOrderId, string OrderNumber, long ShippedBy, DateTime ShippedUtc, int LineCount);

internal sealed class SearchShipmentsValidator : AbstractValidator<SearchShipmentsQuery>
{
    public SearchShipmentsValidator()
    {
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class SearchShipmentsHandler(IDbConnectionFactory connections)
{
    public async Task<PagedResult<ShipmentDto>> HandleAsync(SearchShipmentsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM inventory.search_shipments(@SalesOrderId, @Page, @PageSize);
            SELECT inventory.count_shipments(@SalesOrderId);
            """, new { q.SalesOrderId, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<ShipmentDto>()).ToList();
        return new PagedResult<ShipmentDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
