using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Antiforgery;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;
using RestaurantOrders.Api.Auth;
using RestaurantOrders.Api.Catalog;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<CsvImportService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
builder.Services.AddScoped<IPasswordHasher<Account>, PasswordHasher<Account>>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "RestaurantOrders.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromHours(builder.Configuration.GetValue("Session:Hours", 12));
        options.SlidingExpiration = true;
        options.Events = new CookieAuthenticationEvents
        {
            OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; },
            OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; },
            OnValidatePrincipal = AuthEndpoints.ValidateSessionAsync
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "RestaurantOrders.Csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
var app = builder.Build();
app.UseExceptionHandler();
if (args.Contains("--bootstrap-admin"))
{
    await AuthEndpoints.BootstrapAdministratorAsync(app);
    return;
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = "invalid_csrf", message = "Neplatný bezpečnostní token." });
            return;
        }
    }
    await next(context);
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/database", async (RestaurantDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Ok(new { status = "ok" })
        : Results.Problem("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable));
app.MapAuthEndpoints();
app.MapCatalogEndpoints();
app.Run();

public partial class Program;
