using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Auth;

public sealed record AccountInput(string? Username, string? Password, string? Role, bool Enabled = true);
public sealed record AccountView(Guid Id, string Username, string Role, bool Enabled)
{
    public static AccountView From(Account account) => new(account.Id, account.Username, account.Role.ToString(), account.Enabled);
}

public sealed class AccountService(RestaurantDbContext db, IPasswordHasher<Account> hasher)
{
    public static string Normalize(string? username) => (username ?? "").Trim().ToLowerInvariant();

    public async Task<Account?> AuthenticateAsync(string? username, string? password, CancellationToken ct)
    {
        var account = await db.Accounts.SingleOrDefaultAsync(x => x.Username == Normalize(username), ct);
        if (account is null || !account.Enabled || string.IsNullOrEmpty(password) || password.Length > 256)
            return null;
        var result = hasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed) return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            account.PasswordHash = hasher.HashPassword(account, password);
            await db.SaveChangesAsync(ct);
        }
        return account;
    }

    public async Task<IResult> SaveAsync(Guid? id, AccountInput input, CancellationToken ct, bool bootstrap = false)
    {
        var username = Normalize(input.Username);
        if (username.Length is < 1 or > 100 || username.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '.' && c != '-' && c != '_'))
            return Error("invalid_username", "Přihlašovací jméno musí mít 1–100 znaků: písmena bez diakritiky, číslice, tečka, pomlčka nebo podtržítko.");
        if (input.Role is not ("Administrator" or "Operational"))
            return Error("invalid_role", "Neplatná role účtu.");
        if ((id is null || input.Password is not null) && (input.Password is null || input.Password.Length is < 12 or > 256))
            return Error("invalid_password", "Heslo musí mít 12–256 znaků.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (bootstrap && await db.Accounts.AnyAsync(ct))
            throw new InvalidOperationException("Bootstrap requires an empty accounts table.");
        var account = id is null ? new Account { Username = username, PasswordHash = "" } : await db.Accounts.FindAsync([id.Value], ct);
        if (account is null) return Results.NotFound();
        if (await db.Accounts.AnyAsync(x => x.Username == username && x.Id != account.Id, ct))
            return Error("username_exists", "Přihlašovací jméno již existuje.", 409);
        var role = Enum.Parse<AccountRole>(input.Role);
        if (id is not null && account.Enabled && account.Role == AccountRole.Administrator &&
            (!input.Enabled || role != AccountRole.Administrator) &&
            !await db.Accounts.AnyAsync(x => x.Id != account.Id && x.Enabled && x.Role == AccountRole.Administrator, ct))
            return Error("last_administrator", "Poslední aktivní správce musí zůstat povolen.", 409);
        account.Username = username;
        account.Role = role;
        account.Enabled = input.Enabled;
        if (input.Password is not null) account.PasswordHash = hasher.HashPassword(account, input.Password);
        if (id is null) db.Accounts.Add(account);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Error("account_conflict", "Účet byl současně změněn. Obnovte seznam a zkuste to znovu.", 409);
        }
        return Results.Ok(AccountView.From(account));
    }

    public async Task BootstrapAsync(string? username, string? password)
    {
        if (await db.Accounts.AnyAsync()) throw new InvalidOperationException("Bootstrap requires an empty accounts table.");
        var result = await SaveAsync(null, new(username, password, "Administrator"), CancellationToken.None, bootstrap: true);
        if (result is not Microsoft.AspNetCore.Http.HttpResults.Ok<AccountView>)
            throw new InvalidOperationException("Bootstrap failed. Use a valid username and a password of 12–256 characters.");
    }

    private static IResult Error(string code, string message, int status = 400) => Results.Json(new { code, message }, statusCode: status);
}
