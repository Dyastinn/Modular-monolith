using System.Data.Common;
using Erp.Shared.Common.Data;
using Microsoft.Data.SqlClient;

namespace Erp.Shared.Infrastructure.Data;

// The only type in the whole solution that names SqlConnection.
public sealed class SqlConnectionFactory(string connectionString)
    : IDbConnectionFactory
{
    public DbConnection Create()
        => new SqlConnection(connectionString);
}
