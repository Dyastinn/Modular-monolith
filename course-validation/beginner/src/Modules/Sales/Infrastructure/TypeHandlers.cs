using System.Data;
using Dapper;
using Erp.Modules.Sales.Domain;

namespace Erp.Modules.Sales.Infrastructure;

// The plan shows only SalesOrderIdTypeHandler; CustomerId and ProductId are also passed to Dapper
// (order.CustomerId, line.ProductId) so they need handlers too — otherwise Dapper throws at runtime.
public sealed class SalesOrderIdTypeHandler : SqlMapper.TypeHandler<SalesOrderId>
{
    public override SalesOrderId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, SalesOrderId value) => p.Value = value.Value;
}

public sealed class CustomerIdTypeHandler : SqlMapper.TypeHandler<CustomerId>
{
    public override CustomerId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, CustomerId value) => p.Value = value.Value;
}

public sealed class ProductIdTypeHandler : SqlMapper.TypeHandler<ProductId>
{
    public override ProductId Parse(object value) => new((long)value);
    public override void SetValue(IDbDataParameter p, ProductId value) => p.Value = value.Value;
}
