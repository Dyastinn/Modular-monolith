using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Yasda.Web.Services;

public sealed record Notice(string Text, bool IsError, string? Code);

// The status line under the navigation: what just happened ("User saved.") or what went wrong, with its error code.
// It stays through the page change a save makes by itself (code calling NavigateTo), and clears when the person
// follows a link somewhere else.
public sealed class Notices : IDisposable
{
    private readonly NavigationManager _nav;

    public Notices(NavigationManager nav)
    {
        _nav = nav;
        _nav.LocationChanged += OnLocationChanged;
    }

    public Notice? Current { get; private set; }
    public event Action? Changed;

    public void Success(string text) => Set(new Notice(text, false, null));
    public void Error(string text, string? code = null) => Set(new Notice(text, true, code));
    public void Clear() => Set(null);

    private void Set(Notice? notice)
    {
        Current = notice;
        Changed?.Invoke();
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (e.IsNavigationIntercepted && Current is not null) Clear();
    }

    public void Dispose() => _nav.LocationChanged -= OnLocationChanged;
}
