using Erp.Shared.BuildingBlocks;

namespace Erp.Shared.Infrastructure;

// The real clock. Registered in AddSharedInfrastructure; a test can substitute a fixed IClock instead.
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
