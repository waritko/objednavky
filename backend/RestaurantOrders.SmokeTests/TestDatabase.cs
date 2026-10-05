using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

internal sealed class TestDatabase(bool sqlServer) : IAsyncDisposable
{
    private readonly string name = $"RestaurantOrders_Test_{Guid.NewGuid():N}";
    private readonly string path = Path.Combine(Path.GetTempPath(), $"restaurant-test-{Guid.NewGuid():N}.db");
    public string Provider => sqlServer ? "SqlServer" : "Sqlite";
    public string Connection { get; private set; } = "";
    private bool created;
    public RestaurantDbContext Context() => sqlServer
        ? new SqlServerRestaurantDbContext(new DbContextOptionsBuilder<SqlServerRestaurantDbContext>().UseSqlServer(Connection).Options)
        : new SqliteRestaurantDbContext(new DbContextOptionsBuilder<SqliteRestaurantDbContext>().UseSqlite(Connection).Options);

    public async Task Initialize()
    {
        if (sqlServer)
        {
            var supplied = Environment.GetEnvironmentVariable("TEST_SQLSERVER_CONNECTION")
                ?? throw new InvalidOperationException("Set TEST_SQLSERVER_CONNECTION to an isolated SQL Server test instance.");
            var builder = new SqlConnectionStringBuilder(supplied) { InitialCatalog = "master" };
            await using var master = new SqlConnection(builder.ConnectionString);
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            // Name is generated here, never accepted from configuration. No existing database is migrated or deleted.
            create.CommandText = $"CREATE DATABASE [{name}]";
            await create.ExecuteNonQueryAsync();
            builder.InitialCatalog = name;
            Connection = builder.ConnectionString;
            created = true;
            await using var version = master.CreateCommand();
            version.CommandText = "SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(30))";
            Console.WriteLine($"SQL Server {await version.ExecuteScalarAsync()} isolated database {name}");
        }
        else { Connection = $"Data Source={path}"; created = true; }
        await using var db = Context();
        await db.Database.MigrateAsync();
        if (!sqlServer)
        {
            await db.Database.OpenConnectionAsync();
            Console.WriteLine($"SQLite {db.Database.GetDbConnection().ServerVersion}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!created) return;
        if (sqlServer)
        {
            // Cleanup is confined to the unique database created by this instance.
            if (!name.StartsWith("RestaurantOrders_Test_", StringComparison.Ordinal) || name.Length != 54)
                throw new InvalidOperationException("Invalid test database cleanup target.");
            SqlConnection.ClearAllPools();
            await using var db = Context();
            await db.Database.EnsureDeletedAsync();
        }
        else
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(path + suffix);
        }
    }
}
