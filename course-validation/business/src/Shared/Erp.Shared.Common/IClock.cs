namespace Erp.Shared.Common;

// A trivial abstraction over "what time is it now?" — injected instead of calling DateTime.UtcNow directly,
// so time-dependent logic (token expiries, Phase 4 §9) can be unit-tested with a pinned clock.
public interface IClock
{
    DateTime UtcNow { get; }
}
