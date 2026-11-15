using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Erp.Web.Shared;

// Keeps a list's view in the address: its search, filters, page and page size. Back, a breadcrumb, a bookmark or a
// link pasted to a colleague opens the list exactly as it was. One per list page (registered as transient).
public sealed class ListAddress(NavigationManager nav, ListMemory memory)
{
    public const int DefaultPageSize = 10;
    private string? _written;

    // Changes when the address changed from outside (the menu, a home tile, Back): the page uses it as the grid's
    // @key, so the grid starts again from the new address.
    public int Version { get; private set; }

    // ?page= counts from 1, like people do; the grid counts from 0.
    public static int PageIndex(int? page) => page is > 1 ? page.Value - 1 : 0;

    // Called on every load: writes the view into the address without adding a step to the browser's history.
    public void Save<T>(GridState<T> state, params (string Name, object? Value)[] filters)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (name, value) in filters)
            values[name] = value is null or "" or false ? null : value;
        var (sort, desc) = GridSort.Of(state);
        values["sort"] = sort;
        values["desc"] = desc ? "true" : null;
        values["page"] = state.Page > 0 ? state.Page + 1 : null;
        values["size"] = state.PageSize != DefaultPageSize ? state.PageSize : null;

        _written = nav.GetUriWithQueryParameters(values);
        memory.Remember(nav.ToBaseRelativePath(_written));
        if (_written != nav.Uri) nav.NavigateTo(_written, replace: true);
    }

    // A column's arrow when the list opens: the sort the address asks for. Goes on each sortable column's InitialDirection.
    public SortDirection Initial(string property)
    {
        var query = new Uri(nav.Uri).Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "", StringComparer.OrdinalIgnoreCase);
        if (query.GetValueOrDefault("sort") != GridSort.Key(property)) return SortDirection.None;
        return string.Equals(query.GetValueOrDefault("desc"), "true", StringComparison.OrdinalIgnoreCase)
            ? SortDirection.Descending : SortDirection.Ascending;
    }

    // Called from the page's OnParametersSet: an address this list didn't write means "show this instead".
    public void Check()
    {
        if (_written is null || nav.Uri == _written) return;
        _written = null;
        Version++;
    }
}

// The last address of each list in this browser tab, for the breadcrumbs: "Sales orders" goes back to the list as
// you left it, not to an empty one.
public sealed class ListMemory
{
    private readonly Dictionary<string, string> _last = new(StringComparer.OrdinalIgnoreCase);

    public void Remember(string relativeUri) => _last[relativeUri.Split('?')[0]] = relativeUri;

    public string Last(string path) => _last.GetValueOrDefault(path, path);
}
