using MudBlazor;

namespace Erp.Web.Shared;

public static class GridPaging
{
    // After a search or filter changes: page 1 of the new results (page 3 of the old ones may not exist).
    public static Task FirstPage<T>(this MudDataGrid<T> grid)
    {
        if (grid.CurrentPage == 0) return grid.ReloadServerData();
        grid.NavigateTo(Page.First);
        return Task.CompletedTask;
    }
}
