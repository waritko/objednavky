using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RestaurantOrders.Api.Persistence;

public sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<SqliteRestaurantDbContext>
{
    public SqliteRestaurantDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqliteRestaurantDbContext>()
            .UseSqlite("Data Source=restaurant-orders.db").Options);
}

public sealed class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerRestaurantDbContext>
{
    public SqlServerRestaurantDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<SqlServerRestaurantDbContext>()
            .UseSqlServer("Server=localhost;Database=RestaurantOrders;Trusted_Connection=True;TrustServerCertificate=True").Options);
}
