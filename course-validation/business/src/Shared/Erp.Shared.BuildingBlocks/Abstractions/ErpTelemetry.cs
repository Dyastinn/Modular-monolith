using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Erp.Shared.BuildingBlocks;

// Plain .NET types: modules record spans and metrics without referencing OpenTelemetry. The host exports them.
public static class ErpTelemetry
{
    public const string Name = "Erp";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
}
