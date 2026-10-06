using System.Net.Http.Json;
using System.Text.Json;

internal static class OrderChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff, HttpClient anonymous)
    {
        static void Assert(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS {name}"); }
        static async Task<JsonElement> Expect(HttpResponseMessage response, int status = 200)
        {
            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != status) throw new Exception($"Orders expected {status}, got {response.StatusCode}: {body}");
            return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
        async Task<Guid> Create(string path, object input) => (await Expect(await admin.PostAsJsonAsync(path, input))).GetProperty("id").GetGuid();
        var table = await Create("/tables", new { name = "Objednávky" });
        var category = await Create("/catalog/categories", new { code = "ORDER", name = "Původní kategorie" });
        var item = await Create("/catalog/items", new { code = "ORDER", name = "Původní jídlo", categoryId = category, priceBeforeVat = 100, vatRate = 12 });
        await Expect(await anonymous.GetAsync($"/tables/{table}/order"), 401);
        await Expect(await staff.GetAsync($"/tables/{table}/order"), 204);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        async Task<JsonElement> Add(Guid unit) => await Expect(await staff.PostAsJsonAsync($"/tables/{table}/units", new { menuItemId = item, unitId = unit }));
        var order = await Add(first);
        var id = order.GetProperty("id").GetGuid();
        order = await Add(first);
        Assert(order.GetProperty("units").GetArrayLength() == 1, "retry does not duplicate a tap");
        order = await Add(second);
        Assert(order.GetProperty("id").GetGuid() == id && order.GetProperty("total").GetDecimal() == 224, "reopen active order and separate units");
        async Task<JsonElement> Note(string? note, Guid? menuItemId = null, int status = 200) =>
            await Expect(await staff.PostAsJsonAsync($"/orders/{id}/note", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), note, menuItemId }), status);
        var noteToken = order.GetProperty("concurrencyToken").GetGuid();
        order = await Note("  Narozeniny  ");
        Assert(order.GetProperty("note").GetString() == "Narozeniny", "order note saved and trimmed");
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/note", new { concurrencyToken = noteToken, note = "Stará změna" }), 409);
        order = await Note("Bez soli", item);
        Assert(order.GetProperty("lineNotes").GetProperty(item.ToString()).GetString() == "Bez soli", "one note shared by item line");
        noteToken = order.GetProperty("concurrencyToken").GetGuid();
        order = await Note("Bez soli", item);
        Assert(order.GetProperty("concurrencyToken").GetGuid() == noteToken, "unchanged note does not create a change");
        await Note("Cizí položka", Guid.NewGuid(), 400);
        await Note(new string('x', 1001), status: 400);
        order = await Note("  ", item);
        Assert(!order.GetProperty("lineNotes").TryGetProperty(item.ToString(), out _), "empty line note clears it");
        order = await Note("Bez soli", item);
        await Expect(await anonymous.PostAsJsonAsync($"/orders/{id}/note", new { concurrencyToken = noteToken, note = "test" }), 401);
        async Task<JsonElement> Change(string action, Guid[]? ids = null, bool all = false, bool confirm = false, int status = 200) =>
            await Expect(await staff.PostAsJsonAsync($"/orders/{id}/{action}", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), unitIds = ids, all, confirmPaidRemoval = confirm }), status);
        var stale = order.GetProperty("concurrencyToken").GetGuid();
        order = await Change("paid", [first]);
        Assert(order.GetProperty("paid").GetDecimal() == 112 && order.GetProperty("unpaid").GetDecimal() == 112, "selected quantity payment");
        var filtered = await Expect(await staff.GetAsync("/orders?unpaid=true&undelivered=true"));
        Assert(filtered.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == id), "filters match individual outstanding units");
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/processed", new { concurrencyToken = stale, all = true }), 409);
        await Change("removed", [first], status: 400);
        order = await Change("removed", [first], confirm: true);
        var removed = order.GetProperty("units").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == first);
        Assert(removed.GetProperty("paidAt").ValueKind == JsonValueKind.String && removed.GetProperty("removedByAccountId").ValueKind == JsonValueKind.String && order.GetProperty("total").GetDecimal() == 112, "paid removal retains payment and actor but lowers total");
        order = await Change("processed", [second]);
        var processedToken = order.GetProperty("concurrencyToken").GetGuid();
        order = await Change("processed", [second]);
        Assert(order.GetProperty("concurrencyToken").GetGuid() == processedToken, "repeated delivery has no effect");
        await Expect(await admin.PutAsJsonAsync($"/catalog/items/{item}", new { code = "ORDER", name = "Nové jídlo", categoryId = category, priceBeforeVat = 200, vatRate = 12 }));
        order = await Add(Guid.NewGuid());
        Assert(order.GetProperty("total").GetDecimal() == 336 && order.GetProperty("undeliveredCount").GetInt32() == 1, "new price applies only to new outstanding unit");
        Assert(order.GetProperty("lineNotes").GetProperty(item.ToString()).GetString() == "Bez soli", "line note survives status changes and new quantities");
        order = await Change("paid", all: true);
        Assert(order.GetProperty("state").GetString() == "Active", "payment alone does not close undelivered order");
        filtered = await Expect(await staff.GetAsync("/orders?unpaid=true"));
        Assert(filtered.EnumerateArray().All(x => x.GetProperty("id").GetGuid() != id), "paid order excluded from unpaid filter");
        order = await Change("processed", all: true);
        Assert(order.GetProperty("state").GetString() == "Closed", "automatic closure");
        await Note("Nelze upravit", status: 409);
        await Change("removed", all: true, confirm: true, status: 409);
        await Expect(await staff.GetAsync($"/tables/{table}/order"), 204);
        order = await Add(Guid.NewGuid());
        Assert(order.GetProperty("id").GetGuid() != id, "closed table starts a new order");
        var old = await Expect(await staff.GetAsync($"/orders/{id}"));
        Assert(old.GetProperty("units")[0].GetProperty("itemName").GetString() == "Původní jídlo", "history preserves original name");
        Assert(old.GetProperty("note").GetString() == "Narozeniny" && old.GetProperty("lineNotes").GetProperty(item.ToString()).GetString() == "Bez soli", "history preserves order and line notes");
        var history = await Expect(await staff.GetAsync($"/orders/history?tableId={table}&pageSize=1"));
        Assert(history.GetProperty("total").GetInt32() == 1 && history.GetProperty("orders")[0].GetProperty("id").GetGuid() == id, "paginated closed history");
        await Expect(await staff.GetAsync("/orders/history?page=0"), 400);
        var audit = await Expect(await staff.GetAsync($"/orders/{id}/audit"));
        Assert(audit.GetArrayLength() == 14 && audit.EnumerateArray().Count(x => x.GetProperty("action").GetString() == "NoteChanged") == 4 && audit.EnumerateArray().All(x => x.GetProperty("username").GetString() == "waiter"), "audit records each real change and actor without retry duplicates");
        await Expect(await anonymous.GetAsync($"/orders/{id}/audit"), 401);
        await Expect(await admin.PutAsJsonAsync($"/catalog/categories/{category}", new { code = "ORDER", name = "Zakázaná", enabled = false }));
        await Expect(await staff.PostAsJsonAsync($"/tables/{table}/units", new { menuItemId = item, unitId = Guid.NewGuid() }), 400);
        Assert(true, "disabled category prevents ordering");
    }
}
