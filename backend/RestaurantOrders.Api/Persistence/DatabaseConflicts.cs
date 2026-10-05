using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace RestaurantOrders.Api.Persistence;

public static class DatabaseConflicts
{
    // EF's SQL Server execution strategy can wrap deadlocks in InvalidOperationException.
    public static bool IsConflict(Exception error) => error is DbUpdateConcurrencyException
        || error is SqlException { Number: 1205 or 2601 or 2627 or 3960 }
        || error is SqliteException { SqliteErrorCode: 5 or 6 or 19 }
        || (error.InnerException is not null && IsConflict(error.InnerException));
}
