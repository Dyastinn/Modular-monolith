namespace Erp.Modules.Sales.Domain;

public enum SalesOrderStatus { Draft, Submitted, Cancelled, Completed }

public static class SalesOrderStatusRules
{
    private static readonly Dictionary<SalesOrderStatus, SalesOrderStatus[]> _allowed = new()
    {
        [SalesOrderStatus.Draft]     = new[] { SalesOrderStatus.Submitted, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Submitted] = new[] { SalesOrderStatus.Completed, SalesOrderStatus.Cancelled },
        [SalesOrderStatus.Cancelled] = Array.Empty<SalesOrderStatus>(),   // final — nowhere to go
        [SalesOrderStatus.Completed] = Array.Empty<SalesOrderStatus>(),   // final — nowhere to go
    };

    // An extension method, so you can write the readable `Status.CanTransitionTo(Cancelled)`.
    public static bool CanTransitionTo(this SalesOrderStatus from, SalesOrderStatus to) =>
        _allowed[from].Contains(to);
}
