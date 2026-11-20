using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Yasda.Shared.Infrastructure;

// How many messages of one module's outbox are waiting, and how many were given up on.
public sealed record OutboxCounts(long Pending, long Parked);

public static partial class OutboxTable
{
    // The schema name goes into SQL text (a table name can't be a parameter), so only a plain name is accepted.
    public static string NameOf(string schema)
    {
        if (!SafeIdentifier().IsMatch(schema)) throw new ArgumentException($"Bad schema name '{schema}'.");
        return $"{schema}.outbox_message";
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]*$")]
    private static partial Regex SafeIdentifier();
}

// Pending is what the dispatcher will still try. Parked is what it has given up on (MaxAttempts failures): nobody is
// working on those any more, so someone has to be told. The dispatcher publishes both as metrics, and a health check
// reports the parked ones.
public sealed class OutboxBacklog(NpgsqlDataSource dataSource, IConfiguration config)
{
    private readonly OutboxOptions _options = config.GetSection("Outbox").Get<OutboxOptions>() ?? new();

    public async Task<OutboxCounts> CountAsync(string schema, CancellationToken ct)
    {
        var table = OutboxTable.NameOf(schema);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QuerySingleAsync<OutboxCounts>(new CommandDefinition(
            $"""
            SELECT COUNT(*) FILTER (WHERE attempts < @MaxAttempts) AS Pending,
                   COUNT(*) FILTER (WHERE attempts >= @MaxAttempts) AS Parked
            FROM {table}
            WHERE processed_utc IS NULL
            """,
            new { _options.MaxAttempts }, cancellationToken: ct));
    }
}
