using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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

            await StartAndStop(database, proxiedHttps: true);
            if (await db.Accounts.CountAsync() != 2 ||
                await accounts.AuthenticateAsync("jana", "Changed-password-123", default) is null ||
                await accounts.AuthenticateAsync("jana", "Lucie", default) is not null ||
                !await db.Tables.AnyAsync(x => x.Id == table.Id && x.Name == table.Name))
                throw new Exception("Restart changed existing accounts or data.");
            Console.WriteLine($"PASS startup with {(createDatabase ? "empty" : "missing")} database and restart preservation ({database.Provider})");
        }
        // Upgrade a populated database from before notes were introduced.
        await using var legacyDatabase = new TestDatabase(sqlServer);
        await legacyDatabase.Initialize(migrate: false);
        await using var legacyDb = legacyDatabase.Context();
        await legacyDb.GetService<IMigrator>().MigrateAsync(legacyDb.Database.GetMigrations().First());
        var legacyActor = new Account { Username = "legacy", PasswordHash = "unused" };
        var legacyTable = new RestaurantTable { Name = "Legacy table" };
        legacyDb.Accounts.Add(legacyActor);
        legacyDb.Tables.Add(legacyTable);
        await legacyDb.SaveChangesAsync();
        var legacyId = Guid.NewGuid();
        var legacyToken = Guid.NewGuid();
        var legacyOpened = DateTimeOffset.UtcNow;
        await legacyDb.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Orders (Id, TableId, ActiveTableId, CreatedByAccountId, OpenedAt, State, ConcurrencyToken) VALUES ({legacyId}, {legacyTable.Id}, {legacyTable.Id}, {legacyActor.Id}, {legacyOpened}, {"Active"}, {legacyToken})");
        await legacyDb.Database.MigrateAsync();
        var preserved = await legacyDb.Orders.SingleAsync(x => x.Id == legacyId);
        if (preserved.Note is not null || preserved.LineNotesJson != "{}" || preserved.ConcurrencyToken != legacyToken)
            throw new Exception("Notes migration did not preserve existing orders with empty notes.");
        Console.WriteLine($"PASS notes migration preserves existing orders ({legacyDatabase.Provider})");
    }

    private static async Task StartAndStop(TestDatabase database, bool proxiedHttps = false)
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
        start.Environment["ASPNETCORE_ENVIRONMENT"] = proxiedHttps ? "Production" : "Development";
        start.Environment["DOTNET_ENVIRONMENT"] = start.Environment["ASPNETCORE_ENVIRONMENT"];
        start.Environment["ASPNETCORE_URLS"] = address;
        start.Environment["Database__Provider"] = database.Provider;
        start.Environment["ConnectionStrings__RestaurantOrders"] = database.Connection;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        try
        {
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(2) };
            for (var attempt = 0; attempt < 300 && !process.HasExited; attempt++)
            {
                try
                {
                    if ((await client.GetAsync("/health/database")).IsSuccessStatusCode)
                    {
                        if (proxiedHttps) await CheckProxiedAuthentication(client);
                        return;
                    }
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

    private static async Task CheckProxiedAuthentication(HttpClient client)
    {
        // Simulate TLS termination at a trusted loopback proxy with an HTTP backend.
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        using var csrf = await client.GetAsync("/auth/csrf");
        if (!csrf.IsSuccessStatusCode)
            throw new Exception($"Production CSRF behind HTTPS proxy failed: {await csrf.Content.ReadAsStringAsync()}");
        var csrfCookie = SecureCookie(csrf, "RestaurantOrders.Csrf");
        var token = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Add("Cookie", csrfCookie);

        var credentials = new { username = "jana", password = "Changed-password-123" };
        using var missingToken = await client.PostAsJsonAsync("/auth/login", credentials);
        if (missingToken.StatusCode != HttpStatusCode.BadRequest)
            throw new Exception("Production proxy login accepted a missing CSRF token.");

        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);
        using var login = await client.PostAsJsonAsync("/auth/login", credentials);
        if (!login.IsSuccessStatusCode)
            throw new Exception($"Production login behind HTTPS proxy failed: {await login.Content.ReadAsStringAsync()}");
        var sessionCookie = SecureCookie(login, "RestaurantOrders.Session");
        client.DefaultRequestHeaders.Remove("Cookie");
        // Forward the cookies explicitly because the test connection itself is HTTP.
        client.DefaultRequestHeaders.Add("Cookie", $"{csrfCookie}; {sessionCookie}");
        using var me = await client.GetAsync("/auth/me");
        if (!me.IsSuccessStatusCode)
            throw new Exception("Production session behind HTTPS proxy was not authenticated.");
        Console.WriteLine("PASS production HTTPS proxy CSRF, secure cookies and authenticated session");
    }

    private static string SecureCookie(HttpResponseMessage response, string name)
    {
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        if (!cookie.Split(';').Any(part => part.Trim().Equals("secure", StringComparison.OrdinalIgnoreCase)))
            throw new Exception($"Production {name} cookie is not secure.");
        return cookie.Split(';')[0];
    }
}
