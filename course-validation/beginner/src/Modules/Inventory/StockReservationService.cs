using System.Data;
using Dapper;
using Erp.Shared.Common.Contracts;

namespace Erp.Modules.Inventory;

// Lives INSIDE the Inventory module; Sales only ever sees IStockReservation (in Shared.Common).
public sealed class StockReservationService : IStockReservation
{
    public async Task<bool> TryReserveAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;
        var rows = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "inventory.usp_Stock_Reserve",
            new { ProductId = productId, Qty = quantity },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows == 1;   // 1 = reserved; 0 = insufficient stock
    }

    public async Task ReleaseAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;
        await conn.ExecuteAsync(new CommandDefinition(
            "inventory.usp_Stock_Release",
            new { ProductId = productId, Qty = quantity },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }

    public async Task DeductAsync(long productId, int quantity, IDbTransaction tx, CancellationToken ct)
    {
        var conn = tx.Connection!;
        await conn.ExecuteAsync(new CommandDefinition(
            "inventory.usp_Stock_Deduct",
            new { ProductId = productId, Qty = quantity },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
