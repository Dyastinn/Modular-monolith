using Dapper;
using Erp.Modules.Inventory.Infrastructure;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Erp.Integration.Tests;

// The most important integration test in the suite: the reservation race (Phase 12 §4). It proves the
// atomic SQL guard works — the bug unit tests structurally cannot catch. Uses the CustomWebAppFactory
// fixture only to guarantee Erp_Test exists and is seeded (item 3: OnHand=8, Reserved=0).
[Collection("Integration")]
public class InventoryReservationTests
{
    private const long RestrictedKitId = 3;
    public InventoryReservationTests(CustomWebAppFactory _) { /* boot ensures the DB is created + seeded */ }

    [Fact]
    public async Task Two_concurrent_reservations_for_the_last_units_only_one_wins()
    {
        await ResetReservation(RestrictedKitId);

        // Two callers each want 5 of the 8 available — only one can win.
        var a = ReserveInOwnCommittedTransaction(RestrictedKitId, 5);
        var b = ReserveInOwnCommittedTransaction(RestrictedKitId, 5);
        bool[] results = await Task.WhenAll(a, b);

        results.Count(r => r).Should().Be(1);                   // exactly one succeeded
        (await GetReserved(RestrictedKitId)).Should().Be(5);    // the winner reserved 5; 3 remain available

        await ResetReservation(RestrictedKitId);                // manual cleanup — we committed
    }

    private static async Task<bool> ReserveInOwnCommittedTransaction(long itemId, int qty)
    {
        await using var conn = new SqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.OpenAsync();
        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        var service = new StockReservationService();          // the REAL Inventory implementation
        var reserved = await service.TryReserveAsync(itemId, qty, tx, default);
        await tx.CommitAsync();                                // COMMIT — so the race is actually observable
        return reserved;
    }

    private static async Task<int> GetReserved(long itemId)
    {
        await using var conn = new SqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.OpenAsync();
        return await conn.ExecuteScalarAsync<int>(
            "SELECT Reserved FROM inventory.Inventory WHERE ItemId=@itemId", new { itemId });
    }

    private static async Task ResetReservation(long itemId)
    {
        await using var conn = new SqlConnection(CustomWebAppFactory.TestConnectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE inventory.Inventory SET Reserved = 0 WHERE ItemId=@itemId", new { itemId });
    }
}
