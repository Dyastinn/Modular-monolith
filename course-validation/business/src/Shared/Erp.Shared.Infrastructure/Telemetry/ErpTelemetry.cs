using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Erp.Shared.Infrastructure;

public static class ErpTelemetry
{
    public const string Name = "Erp";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
}
