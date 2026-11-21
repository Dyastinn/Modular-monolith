using System.Data;
using System.Globalization;
using Dapper;

namespace Yasda.Shared.Infrastructure;

// PostgreSQL `time` columns are a time of day; this maps them to TimeOnly.
public sealed class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value;
    }

    public override TimeOnly Parse(object value) => value switch
    {
        TimeOnly t => t,
        TimeSpan span => TimeOnly.FromTimeSpan(span),
        _ => TimeOnly.Parse(value.ToString()!, CultureInfo.InvariantCulture)
    };
}
