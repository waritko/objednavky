using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
var database = Path.Combine(Path.GetTempPath(), $"restaurant-auth-{Guid.NewGuid():N}.db");
var connection = $"Data Source={database}";
var options = new DbContextOptionsBuilder<SqliteRestaurantDbContext>().UseSqlite(connection).Options;
await using (var db = new SqliteRestaurantDbContext(options)) await db.Database.MigrateAsync();
var api = Path.Combine(root, "RestaurantOrders.Api/bin/Debug/net10.0/RestaurantOrders.Api.dll");
var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var address = $"http://127.0.0.1:{port}";
Process Start(bool bootstrap)
{
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add(api);
    if (bootstrap) start.ArgumentList.Add("--bootstrap-admin");
    start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
    start.Environment["ASPNETCORE_URLS"] = address;
    start.Environment["Database__Provider"] = "Sqlite";
    start.Environment["ConnectionStrings__RestaurantOrders"] = connection;
    start.Environment["Bootstrap__Username"] = "Admin";
    start.Environment["Bootstrap__Password"] = "Smoke-test-password-123";
    var process = Process.Start(start)!;
    process.OutputDataReceived += (_, e) => { if (e.Data is not null && (e.Data.Contains("Exception") || e.Data.Contains("fail:"))) Console.WriteLine(e.Data); };
    process.ErrorDataReceived += (_, e) => { if (!bootstrap && e.Data is not null) Console.Error.WriteLine(e.Data); };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    return process;
}
HttpClient Client() => new(new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false }) { BaseAddress = new Uri(address) };
async Task Token(HttpClient client)
{
    var json = await client.GetFromJsonAsync<JsonElement>("/auth/csrf");
    client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
    client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", json.GetProperty("token").GetString());
}
async Task Check(HttpResponseMessage response, int expected, string name)
{
    if ((int)response.StatusCode != expected) throw new Exception($"{name}: expected {expected}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    Console.WriteLine($"PASS {name}");
}
Process? server = null;
try
{
    using (var bootstrap = Start(true))
    {
        await bootstrap.WaitForExitAsync();
        if (bootstrap.ExitCode != 0) throw new Exception("Bootstrap failed");
    }
    using (var bootstrap = Start(true))
    {
        await bootstrap.WaitForExitAsync();
        if (bootstrap.ExitCode == 0) throw new Exception("Bootstrap accepted existing accounts");
    }
    Console.WriteLine("PASS bootstrap and repeat rejection");
    server = Start(false);
    using var admin = Client();
    using var staff = Client();
    using var anonymous = Client();
    var ready = false;
    for (var attempt = 0; attempt < 100; attempt++)
    {
        try { ready = (await admin.GetAsync("/health")).IsSuccessStatusCode; } catch (HttpRequestException) { }
        if (ready) break;
        await Task.Delay(100);
    }
    if (!ready) throw new Exception("API did not start");
    await Check(await anonymous.GetAsync("/accounts"), 401, "anonymous administration denied");
    await Check(await admin.PostAsJsonAsync("/auth/login", new { username = "admin", password = "Smoke-test-password-123" }), 400, "login requires CSRF");
    await Token(admin);
    await Check(await admin.PostAsJsonAsync("/auth/login", new { username = "admin", password = "incorrect" }), 401, "bad password denied");
    await Check(await admin.PostAsJsonAsync("/auth/login", new { username = " ADMIN ", password = "Smoke-test-password-123" }), 200, "administrator login");
    await Token(admin);
    await Check(await admin.GetAsync("/accounts"), 200, "administrator access");
    var me = await admin.GetFromJsonAsync<JsonElement>("/auth/me");
    var adminId = me.GetProperty("id").GetGuid();
    await Check(await admin.PutAsJsonAsync($"/accounts/{adminId}", new { username = "admin", role = "Operational", enabled = true }), 409, "last administrator protected");
    var create = await admin.PostAsJsonAsync("/accounts", new { username = "Waiter", password = "Waiter-password-123", role = "Operational" });
    await Check(create, 200, "create operational account");
    var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    await Check(await admin.PostAsJsonAsync("/accounts", new { username = " WAITER ", password = "Waiter-password-123", role = "Operational" }), 409, "duplicate normalized username");
    await Token(staff);
    await Check(await staff.PostAsJsonAsync("/auth/login", new { username = "waiter", password = "Waiter-password-123" }), 200, "operational login");
    await Token(staff);
    await Check(await staff.GetAsync("/accounts"), 403, "operational administration denied");
    await Check(await staff.PostAsJsonAsync("/accounts", new { username = "intruder", password = "Waiter-password-123", role = "Administrator" }), 403, "operational account creation denied");
    await CatalogChecks.Run(admin, staff, anonymous);
    await ImportChecks.Run(admin, staff, anonymous, Path.Combine(root, "../ciselnik.csv"));
    await Check(await admin.PutAsJsonAsync($"/accounts/{id}", new { username = "waiter", password = "Changed-password-123", role = "Operational", enabled = true }), 200, "password reset");
    await Check(await staff.GetAsync("/auth/me"), 401, "password reset revokes session");
    await Token(staff);
    await Check(await staff.PostAsJsonAsync("/auth/login", new { username = "waiter", password = "Changed-password-123" }), 200, "new password login");
    await Check(await admin.PutAsJsonAsync($"/accounts/{id}", new { username = "waiter", role = "Operational", enabled = false }), 200, "disable account");
    await Check(await staff.GetAsync("/auth/me"), 401, "disabled account session denied");
    await Check(await admin.PostAsync("/auth/logout", null), 204, "logout");
    await Check(await admin.GetAsync("/auth/me"), 401, "logout clears session");
    Console.WriteLine("Authentication, catalog and CSV import smoke tests passed (SQLite).");
}
finally
{
    if (server is not null) { if (!server.HasExited) server.Kill(entireProcessTree: true); await server.WaitForExitAsync(); server.Dispose(); }
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(database + suffix);
}
