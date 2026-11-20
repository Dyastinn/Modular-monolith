using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;

namespace Yasda.Modules.Tickets.Infrastructure;

// Passes a list of numbers to a SQL function as one PostgreSQL bigint[]. Dapper's own handling of a list turns it into
// "(@a1, @a2)", which suits an IN clause and breaks a function argument. A null list means "no filter".
internal sealed class BigintArrayParameter(long[]? values) : SqlMapper.ICustomQueryParameter
{
    public void AddParameter(IDbCommand command, string name)
    {
        var parameter = new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        if (values is null) parameter.Value = DBNull.Value;
        else parameter.Value = values;
        command.Parameters.Add(parameter);
    }
}
