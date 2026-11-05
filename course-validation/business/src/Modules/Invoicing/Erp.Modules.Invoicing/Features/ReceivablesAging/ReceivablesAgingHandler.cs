using Dapper;
using Erp.Shared.BuildingBlocks;
using Mediator;

namespace Erp.Modules.Invoicing.Features;

internal sealed record ReceivablesAgingQuery(DateOnly? AsOf) : IQuery<ReceivablesAgingDto>;

internal sealed record AgingRowDto(long CustomerId, string CustomerName, decimal NotYetDue, decimal Days1To30, decimal Days31To60,
    decimal Days61To90, decimal Over90, decimal Total);

internal sealed record ReceivablesAgingDto(DateOnly AsOf, IReadOnlyList<AgingRowDto> Customers, AgingRowDto Totals);

internal sealed class ReceivablesAgingHandler(IDbConnectionFactory connections, IClock clock)
    : IQueryHandler<ReceivablesAgingQuery, ReceivablesAgingDto>
{
    public async ValueTask<ReceivablesAgingDto> Handle(ReceivablesAgingQuery q, CancellationToken ct)
    {
        var asOf = q.AsOf ?? clock.Today;
        await using var conn = connections.Create();
        var rows = (await conn.QueryAsync<AgingRowDto>(new CommandDefinition(
            "SELECT * FROM invoicing.receivables_aging(@AsOf);", new { AsOf = asOf }, cancellationToken: ct))).ToList();

        var totals = new AgingRowDto(0, "Total", rows.Sum(r => r.NotYetDue), rows.Sum(r => r.Days1To30), rows.Sum(r => r.Days31To60),
            rows.Sum(r => r.Days61To90), rows.Sum(r => r.Over90), rows.Sum(r => r.Total));
        return new ReceivablesAgingDto(asOf, rows, totals);
    }
}
