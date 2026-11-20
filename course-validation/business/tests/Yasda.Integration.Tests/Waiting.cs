using Dapper;
using Npgsql;

namespace Yasda.Integration.Tests;

// What a test does when the answer arrives later: events travel through the outbox, so the effect of a change shows up a
// moment after the request that caused it. These wait for it, instead of guessing how long to sleep.
internal static class Waiting
{
    // Reads until the answer is the one wanted, for up to fifteen seconds, and returns the last thing it read.
    public static async Task<T> Eventually<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var value = await read();
        for (var i = 0; i < 75 && !done(value); i++)
        {
            await Task.Delay(200);
            value = await read();
        }
        return value;
    }

    // Every module's outbox has delivered what it was given. A message that keeps failing is parked, not waited for.
    public static async Task OutboxesAreDrainedAsync(NpgsqlConnection conn)
    {
        var tables = (await conn.QueryAsync<string>(
            "SELECT quote_ident(table_schema) || '.outbox_message' FROM information_schema.tables WHERE table_name = 'outbox_message'")).ToList();

        var waiting = "SELECT 0";
        foreach (var table in tables)
            waiting += $" + (SELECT COUNT(*) FROM {table} WHERE processed_utc IS NULL AND attempts < 10)";

        var left = await Eventually(() => conn.ExecuteScalarAsync<long>(waiting), count => count == 0);
        if (left != 0) throw new TimeoutException($"{left} outbox messages were still waiting after 15 seconds.");
    }
}
