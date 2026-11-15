using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Erp.Web.Shared;

// A column of pesos for any MudDataGrid: right-aligned, ₱ and two decimals (Format.Peso), and the grid's mobile
// label from Title. One definition, so every amount in the app lines up and reads the same.
// It writes the PropertyColumn itself, right where the page asks for it: a column wrapped in a component of its own
// would join the grid only after its siblings, and end up last in the row.
public static class MoneyColumn
{
    public static RenderFragment Of<T>(string title, Expression<Func<T, decimal>> property, decimal? total = null, bool warn = false,
        bool? sortable = null, SortDirection initial = SortDirection.None, bool hidden = false) =>
        Build(title, property, property.Compile() is var f ? x => f(x) : null!, Format.Peso, total, warn, sortable, initial, hidden);

    // An amount that may be missing (a cost the viewer may not see): the cell stays empty.
    public static RenderFragment Of<T>(string title, Expression<Func<T, decimal?>> property, decimal? total = null, bool warn = false,
        bool? sortable = null, SortDirection initial = SortDirection.None, bool hidden = false) =>
        Build(title, property, property.Compile(), Format.Peso, total, warn, sortable, initial, hidden);

    // A cost per unit: up to four decimals, the way an average cost is kept.
    public static RenderFragment Cost<T>(string title, Expression<Func<T, decimal?>> property, bool? sortable = null,
        SortDirection initial = SortDirection.None, bool hidden = false) =>
        Build(title, property, property.Compile(), Format.Cost, null, false, sortable, initial, hidden);

    private static RenderFragment Build<T, TValue>(string title, Expression<Func<T, TValue>> property, Func<T, decimal?> amount,
        Func<decimal, string> format, decimal? total, bool warn, bool? sortable, SortDirection initial, bool hidden) => builder =>
    {
        builder.OpenComponent<PropertyColumn<T, TValue>>(0);
        builder.AddComponentParameter(1, nameof(PropertyColumn<T, TValue>.Property), property);
        builder.AddComponentParameter(2, nameof(PropertyColumn<T, TValue>.Title), title);
        builder.AddComponentParameter(3, nameof(PropertyColumn<T, TValue>.HeaderClass), "money");
        builder.AddComponentParameter(4, nameof(PropertyColumn<T, TValue>.FooterClass), "money");
        // Amounts above zero in the error colour: money overdue, a certificate missing.
        builder.AddComponentParameter(5, nameof(PropertyColumn<T, TValue>.CellClassFunc),
            (Func<T, string>)(item => warn && amount(item) > 0 ? "money mud-error-text" : "money"));
        builder.AddComponentParameter(6, nameof(PropertyColumn<T, TValue>.Sortable), sortable);
        builder.AddComponentParameter(7, nameof(PropertyColumn<T, TValue>.InitialDirection), initial);
        builder.AddComponentParameter(8, nameof(PropertyColumn<T, TValue>.Hidden), hidden);
        builder.AddComponentParameter(9, nameof(PropertyColumn<T, TValue>.CellTemplate),
            (RenderFragment<CellContext<T>>)(cell => b => b.AddContent(0, amount(cell.Item) is { } a ? format(a) : "")));
        // A total under the column, in the grid's footer row. Leave it out and the grid has no footer.
        if (total is { } sum)
            builder.AddComponentParameter(10, nameof(PropertyColumn<T, TValue>.FooterTemplate),
                (RenderFragment<FooterContext<T>>)(_ => b => b.AddContent(0, format(sum))));
        builder.CloseComponent();
    };
}
