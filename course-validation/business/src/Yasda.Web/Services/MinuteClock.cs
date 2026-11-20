namespace Yasda.Web.Services;

// Ticks once a minute. A page that shows "1h 10m left" listens, and draws itself again, so the words stay true without
// asking the server (DESIGN.md 5.2: SLA times update once a minute, no ticking seconds).
public sealed class MinuteClock : IDisposable
{
    private readonly Timer _timer;

    public MinuteClock()
    {
        _timer = new Timer(_ => Tick?.Invoke(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public event Action? Tick;

    public void Dispose()
    {
        _timer.Dispose();
    }
}
