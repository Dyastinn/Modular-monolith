using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Erp.Web.Shared;

// Keeps a list's view in the address: its search, filters, page and page size. Back, a breadcrumb, a bookmark or a
// link pasted to a colleague opens the list exactly as it was. One per list page (registered as transient).
public sealed class ListAddress(NavigationManager nav, ListMemory memory)
{
    public const int DefaultPageSize = 10;
    private string? _written;

    // Changes when the address changed from outside (the menu, a home tile, Back): the page uses it as the table's
    // @key, so the table starts again from the new address.
    public int Version { get; private set; }

    // ?page= counts from 1, like people do; the table counts from 0.
    public static int PageIndex(int? page) => page is > 1 ? page.Value - 1 : 0;

    // Called on every load: writes the view into the address without adding a step to the browser's history.
    public void Save(TableState state, params (string Name, object? Value)[] filters)
    {
        var values = new Dictionary<string, object?>();
        foreach (var (name, value) in filters)
            values[name] = value is null or "" or false ? null : value;
        values["page"] = state.Page > 0 ? state.Page + 1 : null;
        values["size"] = state.PageSize != DefaultPageSize ? state.PageSize : null;

        _written = nav.GetUriWithQueryParameters(values);
        memory.Remember(nav.ToBaseRelativePath(_written));
        if (_written != nav.Uri) nav.NavigateTo(_written, replace: true);
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
