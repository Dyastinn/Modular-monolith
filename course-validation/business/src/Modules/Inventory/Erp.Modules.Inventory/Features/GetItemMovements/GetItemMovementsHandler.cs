using Dapper;
using Erp.Modules.Inventory.Domain;
using Erp.Shared.BuildingBlocks;
using FluentValidation;
using Mediator;

namespace Erp.Modules.Inventory.Features;

internal sealed record GetItemMovementsQuery(long ItemId, int Page, int PageSize) : IQuery<PagedResult<StockMovementDto>>;

internal sealed record StockMovementDto(long Id, decimal Quantity, string Kind, string? Reference, string? Reason, long UserId, DateTime OccurredUtc);

internal sealed record StockMovementRow(long Id, decimal Quantity, int Kind, string? Reference, string? Reason, long UserId, DateTime OccurredUtc);

internal sealed class GetItemMovementsValidator : AbstractValidator<GetItemMovementsQuery>
{
    public GetItemMovementsValidator()
    {
        RuleFor(x => x.ItemId).GreaterThan(0);
        RuleFor(x => x.Page).ValidPage();
        RuleFor(x => x.PageSize).ValidPageSize();
    }
}

internal sealed class GetItemMovementsHandler(IDbConnectionFactory connections)
    : IQueryHandler<GetItemMovementsQuery, PagedResult<StockMovementDto>>
{
    public async ValueTask<PagedResult<StockMovementDto>> Handle(GetItemMovementsQuery q, CancellationToken ct)
    {
        await using var conn = connections.Create();
        using var multi = await conn.QueryMultipleAsync(new CommandDefinition(
            """
            SELECT * FROM inventory.get_item_movements(@ItemId, @Page, @PageSize);
            SELECT inventory.count_item_movements(@ItemId);
            """, new { q.ItemId, q.Page, q.PageSize }, cancellationToken: ct));

        var rows = (await multi.ReadAsync<StockMovementRow>())
            .Select(r => new StockMovementDto(r.Id, r.Quantity, ((MovementKind)r.Kind).ToString(), r.Reference, r.Reason, r.UserId, r.OccurredUtc))
            .ToList();
        return new PagedResult<StockMovementDto>(rows, q.Page, q.PageSize, (int)await multi.ReadSingleAsync<long>());
    }
}
