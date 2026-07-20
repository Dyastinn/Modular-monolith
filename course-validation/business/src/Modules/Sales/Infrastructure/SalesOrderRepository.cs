using System.Data;
using Dapper;
using Erp.Shared.BuildingBlocks;
using Erp.Modules.Sales.Domain;

namespace Erp.Modules.Sales.Infrastructure;

public sealed class SalesOrderRepository(IDbConnectionFactory factory) : ISalesOrderRepository
{
    // Flat DB shapes for rehydration (write side).
    private sealed record HeaderRow(
        long Id, string OrderNumber, long CustomerId, int Status,
        long CreatedByUserId, long? ApprovedByUserId, byte[] RowVersion);
    private sealed record LineRow(long ItemId, int Quantity, decimal UnitPrice);

    public async Task<string> NextOrderNumberAsync(CancellationToken ct)
    {
        using var conn = factory.Create();
        return await conn.ExecuteScalarAsync<string>(new CommandDefinition(
            "sales.usp_SalesOrder_NextNumber",
            commandType: CommandType.StoredProcedure, cancellationToken: ct)) ?? string.Empty;
    }

    public async Task<long> AddAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;

        var newId = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
            "sales.usp_SalesOrder_Insert",
            new
            {
                order.OrderNumber,
                CustomerId = order.CustomerId.Value,
                Status = (int)order.Status,
                TotalAmount = order.Total.Amount,
                CreatedByUserId = order.CreatedBy.Value
            },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "sales.usp_SalesOrderLine_Insert",
                new
                {
                    SalesOrderId = newId,
                    ItemId = line.ItemId.Value,
                    Quantity = line.Quantity.Value,
                    UnitPrice = line.UnitPrice.Amount
                },
                transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        return newId;   // hand the DB-assigned id back to the handler
    }

    public async Task<SalesOrder?> GetAsync(SalesOrderId id, CancellationToken ct)
    {
        using var conn = factory.Create();

        var header = await conn.QuerySingleOrDefaultAsync<HeaderRow>(new CommandDefinition(
            "sales.usp_SalesOrder_GetById", new { Id = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
        if (header is null) return null;

        var lineRows = await conn.QueryAsync<LineRow>(new CommandDefinition(
            "sales.usp_SalesOrderLine_GetByOrder", new { SalesOrderId = id.Value },
            commandType: CommandType.StoredProcedure, cancellationToken: ct));

        var lines = lineRows.Select(l =>
            new SalesOrderLine(new ItemId(l.ItemId), new Quantity(l.Quantity), new Money(l.UnitPrice)));

        return SalesOrder.Rehydrate(
            new SalesOrderId(header.Id), header.OrderNumber, new CustomerId(header.CustomerId),
            (SalesOrderStatus)header.Status, new UserId(header.CreatedByUserId),
            header.ApprovedByUserId is { } a ? new UserId(a) : null,
            header.RowVersion ?? Array.Empty<byte>(), lines);
    }

    public async Task UpdateStatusAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var rows = await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "sales.usp_SalesOrder_UpdateStatus",
            new
            {
                Id = order.Id.Value,
                Status = (int)order.Status,
                ApprovedByUserId = order.Status == SalesOrderStatus.Approved ? (long?)order.ApprovedBy?.Value : null,
                RowVersion = order.RowVersion
            },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        if (rows == 0)
            throw new ConcurrencyException("This order was changed by someone else. Reload and try again.");
    }

    public async Task ReplaceLinesAsync(SalesOrder order, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;

        await conn.ExecuteAsync(new CommandDefinition(
            "sales.usp_SalesOrderLine_DeleteByOrder", new { SalesOrderId = order.Id.Value },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        foreach (var line in order.Lines)
            await conn.ExecuteAsync(new CommandDefinition(
                "sales.usp_SalesOrderLine_Insert",
                new { SalesOrderId = order.Id.Value, ItemId = line.ItemId.Value,
                      Quantity = line.Quantity.Value, UnitPrice = line.UnitPrice.Amount },
                transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "sales.usp_SalesOrder_UpdateTotal",
            new { Id = order.Id.Value, TotalAmount = order.Total.Amount },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
