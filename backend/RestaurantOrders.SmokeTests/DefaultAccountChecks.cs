using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Auth;
using RestaurantOrders.Api.Domain;

internal static class DefaultAccountChecks
{
    public static async Task Run(bool sqlServer)
    {
        await using var database = new TestDatabase(sqlServer);
        await database.Initialize();
        await using var db = database.Context();
        var hasher = new PasswordHasher<Account>();
        var accounts = new AccountService(db, hasher);
        await accounts.InitializeDefaultsAsync();

        var jana = await accounts.AuthenticateAsync("jana", "Lucie", default);
        var monami = await accounts.AuthenticateAsync("monami", "Kava", default);
        if (jana is not { Enabled: true, Role: AccountRole.Administrator } ||
            monami is not { Enabled: true, Role: AccountRole.Operational } ||
            await db.Accounts.CountAsync() != 2)
            throw new Exception("Default accounts or credentials are incorrect.");
        if (jana.PasswordHash == "Lucie" || monami.PasswordHash == "Kava")
            throw new Exception("Default passwords must be hashed.");

        jana.PasswordHash = hasher.HashPassword(jana, "Changed-password-123");
        monami.Enabled = false;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        await accounts.InitializeDefaultsAsync();
        if (await db.Accounts.CountAsync() != 2 ||
            await accounts.AuthenticateAsync("jana", "Changed-password-123", default) is null ||
            await accounts.AuthenticateAsync("jana", "Lucie", default) is not null ||
            await accounts.AuthenticateAsync("monami", "Kava", default) is not null)
            throw new Exception("Repeated initialization changed existing accounts.");

        // A partially populated database must not regain default credentials.
        db.Accounts.Remove(await db.Accounts.SingleAsync(x => x.Username == "monami"));
        await db.SaveChangesAsync();
        await accounts.InitializeDefaultsAsync();
        if (await db.Accounts.CountAsync() != 1)
            throw new Exception("Initialization added defaults to a populated database.");
        Console.WriteLine("PASS default account credentials, roles and preservation");
    }
}
