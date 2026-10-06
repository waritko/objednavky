using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Auth;
using RestaurantOrders.Api.Domain;

internal static class StartupChecks
{
    public static async Task Run(bool sqlServer)
    {
        foreach (var createDatabase in new[] { false, true })
        {
            await using var database = new TestDatabase(sqlServer);
            await database.Initialize(migrate: false, createDatabase: createDatabase);
            await StartAndStop(database);

            await using var db = database.Context();
            var accounts = new AccountService(db, new PasswordHasher<Account>());
            if (await accounts.AuthenticateAsync("jana", "Lucie", default) is not { Role: AccountRole.Administrator } ||
                await accounts.AuthenticateAsync("monami", "Kava", default) is not { Role: AccountRole.Operational } ||
                await db.Accounts.CountAsync() != 2 || (await db.Database.GetPendingMigrationsAsync()).Any())
                throw new Exception("Startup did not migrate the database and create default accounts.");

            var jana = await db.Accounts.SingleAsync(x => x.Username == "jana");
            jana.PasswordHash = new PasswordHasher<Account>().HashPassword(jana, "Changed-password-123");
            var table = new RestaurantTable { Name = "Preserved on restart" };
            db.Tables.Add(table);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            await StartAndStop(database);
            if (await db.Accounts.CountAsync() != 2 ||
                await accounts.AuthenticateAsync("jana", "Changed-password-123", default) is null ||
                await accounts.AuthenticateAsync("jana", "Lucie", default) is not null ||
                !await db.Tables.AnyAsync(x => x.Id == table.Id && x.Name == table.Name))
                throw new Exception("Restart changed existing accounts or data.");
            Console.WriteLine($"PASS startup with {(createDatabase ? "empty" : "missing")} database and restart preservation ({database.Provider})");
        }
    }

    private static async Task StartAndStop(TestDatabase database)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var api = Path.Combine(root, $"RestaurantOrders.Api/bin/{configuration}/net10.0/RestaurantOrders.Api.dll");
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var address = $"http://127.0.0.1:{port}";
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(api);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["ASPNETCORE_URLS"] = address;
        start.Environment["Database__Provider"] = database.Provider;
        start.Environment["ConnectionStrings__RestaurantOrders"] = database.Connection;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
            for (var attempt = 0; attempt < 300 && !process.HasExited; attempt++)
            {
                try
                {
                    if ((await client.GetAsync("/health/database")).IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(100);
            }
            throw new Exception("API did not start with an uninitialized database.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            var log = await output + await errors;
            if (log.Contains("fail:") || log.Contains("Unhandled exception")) Console.Error.WriteLine(log);
        }
    }
}
