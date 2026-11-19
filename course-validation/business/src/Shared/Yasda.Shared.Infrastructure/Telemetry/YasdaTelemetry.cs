using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Yasda.Shared.Infrastructure;

public static class YasdaTelemetry
{
    public const string Name = "Yasda";

    public static readonly ActivitySource Source = new(Name);
    public static readonly Meter Meter = new(Name);
}
