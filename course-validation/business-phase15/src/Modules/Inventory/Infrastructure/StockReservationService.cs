using System.Data;
using Dapper;
using Erp.Modules.Inventory.Contracts;

namespace Erp.Modules.Inventory.Infrastructure;

// Inventory owns this (Phase 7 §3). `internal` (Phase 15): outside this assembly only the Contracts surface
// is visible — a consumer that references Inventory still cannot name or new-up this class. The safety is
// entirely in the atomic SQL UPDATE; the C# just reads whether it reported success.
internal sealed class StockReservationService : IStockReservation
{
    public async Task<bool> TryReserveAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct)
    {
        var rows = await tx.Connection!.ExecuteScalarAsync<int>(new CommandDefinition(
            "inventory.usp_Inventory_Reserve",
            new { ItemId = itemId, Qty = qty },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
        return rows == 1;   // proc returns @@ROWCOUNT: 1 = reserved, 0 = not enough stock
    }

    public async Task ReleaseAsync(long itemId, int qty, IDbTransaction tx, CancellationToken ct)
    {
        await tx.Connection!.ExecuteAsync(new CommandDefinition(
            "inventory.usp_Inventory_Release",
            new { ItemId = itemId, Qty = qty },
            transaction: tx, commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
