using System.Net.Http.Json;
using System.Text.Json;

internal static class MoveOrderChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff, HttpClient anonymous)
    {
        static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine($"PASS {name}");
        }
        static async Task<JsonElement> Expect(HttpResponseMessage response, int status = 200)
        {
            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != status) throw new Exception($"Move expected {status}, got {response.StatusCode}: {body}");
            return body.Length == 0 ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
        async Task<int> Table(string name, bool enabled = true) =>
            (await Expect(await admin.PostAsJsonAsync("/tables", new { name, enabled }))).GetProperty("id").GetInt32();
        var source = await Table("Move source");
        var target = await Table("Move target");
        var disabled = await Table("Move disabled", false);
        var category = (await Expect(await admin.PostAsJsonAsync("/catalog/categories", new { code = "MOVE", name = "Move" }))).GetProperty("id").GetInt32();
        var item = (await Expect(await admin.PostAsJsonAsync("/catalog/items", new { code = "MOVE", name = "Move item", categoryId = category, priceBeforeVat = 100, vatRate = 12 }))).GetProperty("id").GetInt32();
        async Task<JsonElement> Add(int table) => await Expect(await staff.PostAsJsonAsync($"/tables/{table}/units", new { menuItemId = item, requestToken = Guid.NewGuid() }));
        var order = await Add(source);
        var id = order.GetProperty("id").GetInt32();
        object Input(int table) => new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), tableId = table };
        await Expect(await anonymous.PostAsJsonAsync($"/orders/{id}/move", Input(target)), 401);
        await Expect(await staff.PostAsJsonAsync($"/orders/{int.MaxValue}/move", Input(target)), 404);
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", Input(disabled)), 400);
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", Input(int.MaxValue)), 400);
        var stale = Input(target);
        order = await Expect(await staff.PostAsJsonAsync($"/orders/{id}/note", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), note = "Keep me" }));
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", stale), 409);
        order = await Expect(await staff.PostAsJsonAsync($"/orders/{id}/paid", new { concurrencyToken = order.GetProperty("concurrencyToken").GetGuid(), all = true }));
        var before = order;
        order = await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", Input(target)));
        Assert(order.GetProperty("id").GetInt32() == id && order.GetProperty("tableId").GetInt32() == target &&
            order.GetProperty("units").GetRawText() == before.GetProperty("units").GetRawText() &&
            order.GetProperty("note").GetString() == "Keep me" && order.GetProperty("paid").GetDecimal() == 112,
            "move retains order identity, units, payments and notes");
        Assert(order.GetProperty("concurrencyToken").GetGuid() != before.GetProperty("concurrencyToken").GetGuid(), "move changes concurrency token");
        await Expect(await staff.GetAsync($"/tables/{source}/order"), 204);
        Assert((await Expect(await staff.GetAsync($"/tables/{target}/order"))).GetProperty("id").GetInt32() == id, "move releases source and occupies destination");
        var token = order.GetProperty("concurrencyToken").GetGuid();
        order = await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", Input(target)));
        Assert(order.GetProperty("concurrencyToken").GetGuid() == token, "same-table move has no effect");
        var audit = await Expect(await staff.GetAsync($"/orders/{id}/audit"));
        var moved = audit.EnumerateArray().Single(x => x.GetProperty("action").GetString() == "Moved");
        var details = JsonDocument.Parse(moved.GetProperty("detailsJson").GetString()!).RootElement;
        Assert(moved.GetProperty("username").GetString() == "waiter" && details.GetProperty("before").GetInt32() == source && details.GetProperty("after").GetInt32() == target, "move audit retains actor and both tables without duplicate events");
        var other = await Add(source);
        await Expect(await staff.PostAsJsonAsync($"/orders/{other.GetProperty("id").GetInt32()}/move", new { concurrencyToken = other.GetProperty("concurrencyToken").GetGuid(), tableId = target }), 409);
        Assert((await Expect(await staff.GetAsync($"/tables/{source}/order"))).GetProperty("id").GetInt32() == other.GetProperty("id").GetInt32(), "occupied destination leaves source unchanged");
        order = await Expect(await staff.PostAsJsonAsync($"/orders/{id}/processed", new { concurrencyToken = token, all = true }));
        await Expect(await staff.PostAsJsonAsync($"/orders/{id}/move", Input(source)), 409);
        // A table with closed history is available, including when two orders compete for it.
        var thirdTable = await Table("Move competitor");
        var third = await Add(thirdTable);
        var attempts = await Task.WhenAll(
            staff.PostAsJsonAsync($"/orders/{other.GetProperty("id").GetInt32()}/move", new { concurrencyToken = other.GetProperty("concurrencyToken").GetGuid(), tableId = target }),
            admin.PostAsJsonAsync($"/orders/{third.GetProperty("id").GetInt32()}/move", new { concurrencyToken = third.GetProperty("concurrencyToken").GetGuid(), tableId = target }));
        Assert(attempts.Count(x => (int)x.StatusCode == 200) == 1 && attempts.Count(x => (int)x.StatusCode == 409) == 1, "concurrent moves allow only one active order at destination with closed history");
    }
}
