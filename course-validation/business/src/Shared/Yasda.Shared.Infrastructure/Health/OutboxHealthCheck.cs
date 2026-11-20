using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yasda.Shared.BuildingBlocks;

namespace Yasda.Shared.Infrastructure;

// Degraded, not unhealthy, when events were given up on: the app still works, but something that should have happened
// (a clock started, a mail queued) did not, and a person has to look at the outbox rows. Unhealthy would have the
// platform restart a perfectly good app.
public sealed class OutboxHealthCheck(IServiceScopeFactory scopes, OutboxBacklog backlog) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        List<string> schemas;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var modules = scope.ServiceProvider.GetServices<IModuleDatabase>();
            schemas = modules.Select(m => m.Schema).ToList();
        }

        var parkedIn = new List<string>();
        foreach (var schema in schemas)
        {
            var counts = await backlog.CountAsync(schema, cancellationToken);
            if (counts.Parked > 0) parkedIn.Add($"{schema}: {counts.Parked}");
        }

        if (parkedIn.Count == 0) return HealthCheckResult.Healthy();
        return HealthCheckResult.Degraded($"Outbox messages were given up on: {string.Join(", ", parkedIn)}.");
    }
}
