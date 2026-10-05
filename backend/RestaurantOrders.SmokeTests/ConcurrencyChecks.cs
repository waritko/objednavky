using System.Net.Http.Json;
using System.Text.Json;

internal static class ConcurrencyChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff)
    {
        static async Task<JsonElement> Read(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode) throw new Exception($"Concurrency HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
        var table = (await Read(await admin.PostAsJsonAsync("/tables", new { name = "Concurrent table" }))).GetProperty("id").GetGuid();
        var category = (await Read(await admin.PostAsJsonAsync("/catalog/categories", new { code = "CONCURRENT", name = "Concurrency" }))).GetProperty("id").GetGuid();
        var item = (await Read(await admin.PostAsJsonAsync("/catalog/items", new { code = "CONCURRENT", name = "Concurrent item", categoryId = category, priceBeforeVat = 10, vatRate = 0 }))).GetProperty("id").GetGuid();
        var ids = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
        async Task Add(Guid unitId)
        {
            for (var attempt = 0; attempt < 12; attempt++)
            {
                using var response = await staff.PostAsJsonAsync($"/tables/{table}/units", new { menuItemId = item, unitId });
                if ((int)response.StatusCode == 409) { await Task.Delay(30 * (attempt + 1)); continue; }
                await Read(response); return;
            }
            throw new Exception("Concurrent addition did not recover from conflict.");
        }
        await Task.WhenAll(ids.Select(Add));
        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Add(ids[0])));
        var order = await staff.GetFromJsonAsync<JsonElement>($"/tables/{table}/order");
        var id = order.GetProperty("id").GetGuid();
        Assert(order.GetProperty("units").GetArrayLength() == 6 && order.GetProperty("total").GetDecimal() == 60, "simultaneous creation/additions and retries lose no units");
        var orders = await staff.GetFromJsonAsync<JsonElement>("/orders");
        Assert(orders.EnumerateArray().Count(x => x.GetProperty("tableId").GetGuid() == table) == 1, "one active order per table under concurrency");
        var token = order.GetProperty("concurrencyToken").GetGuid();
        var responses = await Task.WhenAll(
            staff.PostAsJsonAsync($"/orders/{id}/paid", new { concurrencyToken = token, unitIds = new[] { ids[0] } }),
            admin.PostAsJsonAsync($"/orders/{id}/processed", new { concurrencyToken = token, unitIds = new[] { ids[1] } }));
        Assert(responses.Count(x => x.IsSuccessStatusCode) == 1 && responses.Count(x => (int)x.StatusCode == 409) == 1, "competing status writes return one explicit conflict");
        var failed = responses[0].IsSuccessStatusCode ? "processed" : "paid";
        foreach (var response in responses) response.Dispose();
        order = await staff.GetFromJsonAsync<JsonElement>($"/orders/{id}");
        await Read(await staff.PostAsJsonAsync($"/orders/{id}/{failed}", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), unitIds = new[] { failed == "paid" ? ids[0] : ids[1] } }));
        order = await staff.GetFromJsonAsync<JsonElement>($"/orders/{id}");
        Assert(order.GetProperty("paid").GetDecimal() == 10 && order.GetProperty("undeliveredCount").GetInt32() == 5, "conflict reload preserves both actors' changes");
        // A foreign unit must fail atomically, even when a valid unit appears first.
        var invalid = await staff.PostAsJsonAsync($"/orders/{id}/removed", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), unitIds = new[] { ids[2], Guid.NewGuid() } });
        Assert((int)invalid.StatusCode == 400, "foreign selection is rejected atomically");
        order = await Read(await staff.PostAsJsonAsync($"/orders/{id}/removed", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), all = true, confirmPaidRemoval = true }));
        Assert(order.GetProperty("total").GetDecimal() == 0 && order.GetProperty("state").GetString() == "Closed", "removing final current unit closes zero-value order");
    }
}
