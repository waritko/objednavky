using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Auth;

public static class AuthEndpoints
{
    private const string StampClaim = "account_stamp";
    public sealed record LoginInput(string? Username, string? Password);

    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = antiforgery.GetAndStoreTokens(context).RequestToken });
        });
        app.MapPost("/auth/login", async (LoginInput input, AccountService accounts, HttpContext context, CancellationToken ct) =>
        {
            var account = await accounts.AuthenticateAsync(input.Username, input.Password, ct);
            if (account is null) return Results.Json(new { code = "invalid_credentials", message = "Nesprávné přihlašovací údaje nebo zakázaný účet." }, statusCode: 401);
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.Username),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
                new Claim(StampClaim, Stamp(account))
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
            return Results.Ok(AccountView.From(account));
        }).RequireRateLimiting("login");
        app.MapPost("/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/auth/me", async (ClaimsPrincipal user, RestaurantDbContext db, CancellationToken ct) =>
            Results.Ok(AccountView.From(await db.Accounts.SingleAsync(x => x.Id == Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct))))
            .RequireAuthorization();
        var accounts = app.MapGroup("/accounts").RequireAuthorization(policy => policy.RequireRole("Administrator"));
        accounts.MapGet("", async (RestaurantDbContext db, CancellationToken ct) =>
            (await db.Accounts.AsNoTracking().OrderBy(x => x.Username).ToListAsync(ct)).Select(AccountView.From));
        accounts.MapPost("", (AccountInput input, AccountService service, CancellationToken ct) => service.SaveAsync(null, input, ct));
        accounts.MapPut("/{id:guid}", (Guid id, AccountInput input, AccountService service, CancellationToken ct) => service.SaveAsync(id, input, ct));
    }

    public static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<RestaurantDbContext>();
        var validId = Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
        var account = validId ? await db.Accounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, context.HttpContext.RequestAborted) : null;
        if (account is null || !account.Enabled || context.Principal?.FindFirstValue(StampClaim) != Stamp(account))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    public static async Task BootstrapAdministratorAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().BootstrapAsync(
            app.Configuration["Bootstrap:Username"], app.Configuration["Bootstrap:Password"]);
        app.Logger.LogInformation("Initial administrator created. Remove bootstrap credentials from the environment.");
    }

    private static string Stamp(Account account) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{account.PasswordHash}|{account.Role}|{account.Username}")));
}
