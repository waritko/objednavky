using System.Net.Http.Json;
using System.Text.Json;

internal static class CatalogChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff, HttpClient anonymous)
    {
        static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine($"PASS {name}");
        }
        static async Task<JsonElement> Expect(HttpResponseMessage response, int status)
        {
            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != status) throw new Exception($"Catalog: expected {status}, got {response.StatusCode}: {body}");
            return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
        async Task<JsonElement> Create(string path, object input) => await Expect(await admin.PostAsJsonAsync(path, input), 200);
        foreach (var path in new[] { "/tables", "/catalog/categories", "/catalog/subcategories", "/catalog/items" })
        {
            await Expect(await anonymous.GetAsync(path), 401);
            await Expect(await staff.GetAsync(path), 200);
            await Expect(await staff.PostAsJsonAsync(path, new { }), 403);
            await Expect(await staff.PutAsJsonAsync($"{path}/{Guid.NewGuid()}", new { }), 403);
        }
        await Expect(await staff.PostAsJsonAsync("/catalog/items/assign-subcategory", new { }), 403);
        Assert(true, "catalog role authorization");
        var table = await Create("/tables", new { name = " Terasa ", sortOrder = 20 });
        var tableId = table.GetProperty("id").GetGuid();
        Assert(table.GetProperty("name").GetString() == "Terasa", "trim table name");
        await Create("/tables", new { name = "Sál", sortOrder = -1 });
        var tables = await staff.GetFromJsonAsync<JsonElement>("/tables");
        Assert(tables[0].GetProperty("name").GetString() == "Sál", "table sorting");
        await Expect(await admin.PutAsJsonAsync($"/tables/{tableId}", new { name = "Terasa", enabled = false }), 200);
        tables = await staff.GetFromJsonAsync<JsonElement>("/tables");
        Assert(tables.EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == tableId && !x.GetProperty("enabled").GetBoolean()), "disable preserves table");
        await Expect(await admin.PostAsJsonAsync("/tables", new { name = " " }), 400);
        await Expect(await admin.PutAsJsonAsync($"/tables/{Guid.NewGuid()}", new { name = "Missing" }), 404);
        var category = await Create("/catalog/categories", new { code = " 001A ", name = " Nápoje " });
        var categoryId = category.GetProperty("id").GetGuid();
        Assert(category.GetProperty("code").GetString() == "001A", "preserve padded alphanumeric category code");
        var other = await Create("/catalog/categories", new { code = "02", name = "Jídla" });
        var otherId = other.GetProperty("id").GetGuid();
        await Expect(await admin.PostAsJsonAsync("/catalog/categories", new { code = "001A", name = "Duplicate" }), 409);
        var sub = await Create("/catalog/subcategories", new { categoryId, name = "Teplé" });
        var subId = sub.GetProperty("id").GetGuid();
        await Expect(await admin.PostAsJsonAsync("/catalog/subcategories", new { categoryId = Guid.NewGuid(), name = "Missing" }), 400);
        object Item(decimal price = 10.05m, decimal vat = 10m, Guid? cat = null, Guid? subcategory = null, string code = " 00K1 ", bool enabled = true) =>
            new { code, name = " Káva ", categoryId = cat ?? categoryId, subcategoryId = subcategory, priceBeforeVat = price, vatRate = vat, enabled };
        var item = await Create("/catalog/items", Item());
        var itemId = item.GetProperty("id").GetGuid();
        Assert(item.GetProperty("price").GetDecimal() == 11.06m && item.GetProperty("subcategoryId").ValueKind == JsonValueKind.Null && item.GetProperty("code").GetString() == "00K1", "VAT rounding and direct-category item");
        await Expect(await admin.PostAsJsonAsync("/catalog/items", Item()), 409);
        foreach (var invalid in new[] { Item(price: -1), Item(price: 0.001m), Item(vat: 100.01m), Item(vat: -1), Item(vat: 12.001m), Item(price: decimal.MaxValue) })
            await Expect(await admin.PostAsJsonAsync("/catalog/items", invalid), 400);
        await Expect(await admin.PostAsJsonAsync("/catalog/items", Item(cat: Guid.NewGuid(), code: "missing")), 400);
        await Expect(await admin.PutAsJsonAsync($"/catalog/items/{itemId}", Item(cat: otherId, subcategory: subId)), 400);
        await Expect(await admin.PostAsJsonAsync("/catalog/items/assign-subcategory", new { itemIds = new[] { itemId }, subcategoryId = subId }), 200);
        await Expect(await admin.PutAsJsonAsync($"/catalog/subcategories/{subId}", new { categoryId = otherId, name = "Moved" }), 409);
        var second = await Create("/catalog/items", Item(cat: otherId, code: "food"));
        var secondId = second.GetProperty("id").GetGuid();
        await Expect(await admin.PostAsJsonAsync("/catalog/items/assign-subcategory", new { itemIds = new[] { secondId, itemId }, subcategoryId = subId }), 400);
        var items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        Assert(items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == secondId).GetProperty("subcategoryId").ValueKind == JsonValueKind.Null, "bulk assignment validation is atomic");
        await Expect(await admin.PostAsJsonAsync("/catalog/items/assign-subcategory", new { itemIds = new[] { itemId }, subcategoryId = (Guid?)null }), 200);
        item = await Expect(await admin.PutAsJsonAsync($"/catalog/items/{itemId}", Item(price: 20, vat: 21, cat: otherId, enabled: false)), 200);
        Assert(item.GetProperty("price").GetDecimal() == 24.20m && !item.GetProperty("enabled").GetBoolean(), "edit price and disable item");
        await Expect(await admin.PutAsJsonAsync($"/catalog/categories/{categoryId}", new { code = "001A", name = "Nápoje", enabled = false }), 200);
        await Expect(await admin.PutAsJsonAsync($"/catalog/subcategories/{subId}", new { categoryId, name = "Teplé", enabled = false }), 200);
        Assert(true, "catalog validation, assignment, updates and soft disable");
    }
}
