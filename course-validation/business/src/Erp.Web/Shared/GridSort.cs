using MudBlazor;

namespace Erp.Web.Shared;

// The grid's one sort, as the API names it: a column's property name in camelCase, the way its JSON is written
// ("OrderNumber" -> "orderNumber"). The API keeps the list of names it accepts.
public static class GridSort
{
    public static (string? Column, bool Descending) Of<T>(GridState<T> state) =>
        state.SortDefinitions.FirstOrDefault() is { } s ? (Key(s.SortBy), s.Descending) : (null, false);

    public static string Key(string property) =>
        property.Length == 0 ? property : char.ToLowerInvariant(property[0]) + property[1..];
}
