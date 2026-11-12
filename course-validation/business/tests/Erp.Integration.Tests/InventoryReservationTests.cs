using AwesomeAssertions;
using Dapper;
using Erp.Modules.Inventory.Contracts;
using Erp.Shared.BuildingBlocks;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Erp.Integration.Tests;

// Each caller gets its own DI scope (connection, transaction), like two simultaneous requests.
[Collection("Integration")]
public class InventoryReservationTests(CustomWebAppFactory factory)
{
    private const long RestrictedKitId = 3;   // dev seed: on_hand = 8, reserved = 0

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        await SetReserved(RestrictedKitId, 0);

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
            // disposed without a commit
        }

        (await GetReserved(RestrictedKitId)).Should().Be(0);   // Inventory really joined the caller's transaction
    }

    private async Task<bool> ReserveInOwnCommittedTransaction(long itemId, decimal qty)
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

    private async Task<decimal> GetReserved(long itemId)
    {
        await using var conn = factory.OpenDatabase();
        return await conn.ExecuteScalarAsync<decimal>("SELECT reserved FROM inventory.item WHERE id = @itemId", new { itemId });
    }

    private async Task SetReserved(long itemId, decimal reserved)
    {
        await using var conn = factory.OpenDatabase();
        await conn.ExecuteAsync("UPDATE inventory.item SET reserved = @reserved WHERE id = @itemId", new { itemId, reserved });
    }
}
