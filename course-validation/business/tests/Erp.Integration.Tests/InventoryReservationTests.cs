using AwesomeAssertions;
using Dapper;
using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Integration.Tests;

// The reservation race: the bug unit tests structurally cannot catch. Each caller gets its own DI scope — its
// own request, connection and transaction — exactly as two simultaneous HTTP requests would.
[Collection("Integration")]
public class InventoryReservationTests(CustomWebAppFactory factory)
{
    private const long RestrictedKitId = 3;   // dev seed: on_hand = 8, reserved = 0

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        await SetReserved(RestrictedKitId, 0);

        // Two callers each want 5 of the 8 available — only one can win.
        bool[] results = await Task.WhenAll(
            ReserveInOwnCommittedTransaction(RestrictedKitId, 5),
            ReserveInOwnCommittedTransaction(RestrictedKitId, 5));

        results.Count(r => r).Should().Be(1);
        (await GetReserved(RestrictedKitId)).Should().Be(5);   // the winner's 5; 3 still available

        await SetReserved(RestrictedKitId, 0);
    }

    [Fact]
    public async Task A_reservation_rolls_back_with_the_callers_transaction()
    {
        await SetReserved(RestrictedKitId, 0);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var conn = scope.ServiceProvider.GetRequiredService<ISharedConnection>().Connection;
            await conn.OpenAsync();
            await using var tx = await conn.BeginTransactionAsync();
            var stock = scope.ServiceProvider.GetRequiredService<IStockReservation>();

            (await stock.TryReserveAsync(RestrictedKitId, 5, tx, default)).Should().BeTrue();
            // no commit: the caller's transaction is disposed → rolled back
        }

        (await GetReserved(RestrictedKitId)).Should().Be(0);   // Inventory really joined the caller's transaction
    }

    private async Task<bool> ReserveInOwnCommittedTransaction(long itemId, int qty)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var conn = scope.ServiceProvider.GetRequiredService<ISharedConnection>().Connection;
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        var stock = scope.ServiceProvider.GetRequiredService<IStockReservation>();   // the public contract only

        var reserved = await stock.TryReserveAsync(itemId, qty, tx, default);
        await tx.CommitAsync();   // commit, so the race is real
        return reserved;
    }

    private static async Task<int> GetReserved(long itemId)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT reserved FROM inventory.stock_item WHERE item_id = @itemId", new { itemId });
    }

    private static async Task SetReserved(long itemId, int reserved)
    {
        await using var conn = new NpgsqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.ExecuteAsync(
            "UPDATE inventory.stock_item SET reserved = @reserved WHERE item_id = @itemId", new { itemId, reserved });
    }
}
