using System.Data.Common;

namespace Erp.Shared.Common.Data;

// Hands out connections. Reads call Create() and open a plain connection; writes open one and start an
// explicit transaction on it, right there in the handler (Phase 3b).
public interface IDbConnectionFactory
{
    DbConnection Create();   // returns an unopened connection; the caller opens it
}
