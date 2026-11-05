using Erp.Shared.BuildingBlocks;

namespace Erp.Shared.Infrastructure;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
