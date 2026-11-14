using MudBlazor;

namespace Erp.Web.Shared;

// The app's tables, one look decided once (like AppFields): compact rows, the row under the pointer highlighted,
// and alternating row colours, so the eye stays on the right row across a wide table.

public class AppTable<T> : MudTable<T>
{
    public AppTable() { Outlined = true; Striped = true; Hover = true; Dense = true; }

    // After a search or filter changes: page 1 of the new results (page 3 of the old ones may not exist).
    public Task FirstPage()
    {
        if (CurrentPage == 0) return ReloadServerData();
        NavigateTo(0);
        return Task.CompletedTask;
    }
}

// Simple tables often sit inside a card that already has a border, so Outlined stays the page's choice here.
public class AppSimpleTable : MudSimpleTable
{
    public AppSimpleTable() { Striped = true; Hover = true; Dense = true; }
}
