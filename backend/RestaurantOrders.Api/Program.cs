using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

var builder = WebApplication.CreateBuilder(args);
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connection = builder.Configuration.GetConnectionString("RestaurantOrders")
    ?? throw new InvalidOperationException("ConnectionStrings:RestaurantOrders is required.");

switch (provider.ToLowerInvariant())
{
    case "sqlite":
        builder.Services.AddDbContext<SqliteRestaurantDbContext>(options => options.UseSqlite(connection));
        builder.Services.AddScoped<RestaurantDbContext>(services => services.GetRequiredService<SqliteRestaurantDbContext>());
        break;
    case "sqlserver":
        builder.Services.AddDbContext<SqlServerRestaurantDbContext>(options => options.UseSqlServer(connection));
        builder.Services.AddScoped<RestaurantDbContext>(services => services.GetRequiredService<SqlServerRestaurantDbContext>());
        break;
    default:
        throw new InvalidOperationException($"Unsupported database provider '{provider}'. Use Sqlite or SqlServer.");
}

builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/database", async (RestaurantDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable));
app.Run();

public partial class Program;
