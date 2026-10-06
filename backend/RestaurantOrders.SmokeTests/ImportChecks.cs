using System.Net.Http.Json;
using System.Text.Json;
using RestaurantOrders.Api.Catalog;

internal static class ImportChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff, HttpClient anonymous, string samplePath)
    {
        static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine($"PASS {name}");
        }
        static async Task<JsonElement> Expect(HttpResponseMessage response, int status = 200)
        {
            var body = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode != status) throw new Exception($"Import: expected {status}, got {response.StatusCode}: {body}");
            return string.IsNullOrEmpty(body) ? default : JsonDocument.Parse(body).RootElement.Clone();
        }
        var csv = await File.ReadAllTextAsync(samplePath);
        var parsed = CsvImportService.Parse(csv);
        Assert(parsed.Rows.Count == 201 && parsed.EmptyRows == 9 && parsed.Errors.Count == 0 && parsed.Rows.Select(x => x.CategoryCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 11, "sample CSV: 201 items, 9 empty rows, 11 categories ignoring case");
        var fixture = CsvImportService.Header + "\n 00X ,\" Kešu, \"\"pražené\"\" \", IMP ,10.05,10\n,,,,\ninvalid,Name,A,-1,12\ndup,One,A,1,12\n dup ,Two,A,2,12\nwrong,columns\n";
        parsed = CsvImportService.Parse(fixture);
        Assert(parsed.Rows.Count == 1 && parsed.Rows[0].Code == "00X" && parsed.Rows[0].Name == "Kešu, \"pražené\"" && parsed.Rows[0].Price == 11.06m && parsed.Errors.Select(x => x.Row).SequenceEqual(new long[] { 4, 5, 6, 7 }), "CSV quoting, trimming, rounding, duplicate and row errors");
        Assert(CsvImportService.Parse("wrong\n").Errors.Count == 1 && CsvImportService.Parse(CsvImportService.Header + "\n\"unclosed").Errors.Count == 1, "invalid header and malformed quotes");
        foreach (var path in new[] { "/catalog/import/preview", "/catalog/import/commit" })
        {
            await Expect(await staff.PostAsJsonAsync(path, new { }), 403);
            await Expect(await anonymous.PostAsJsonAsync(path, new { }), 401);
        }
        Assert((int)(await staff.GetAsync("/catalog/import/template")).StatusCode == 403, "import role protection");
        Assert((await admin.GetStringAsync("/catalog/import/template")).StartsWith(CsvImportService.Header), "CSV template download");
        var before = (await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength();
        var preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = fixture }));
        Assert((await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength() == before, "preview does not write catalog");
        var token = preview.GetProperty("token").GetString();
        await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = "tampered" }), 400);
        var result = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token }));
        Assert(result.GetProperty("created").GetInt32() == 1, "commit only preview-valid rows");
        var items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        var item = items.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "00X");
        var id = item.GetProperty("id").GetGuid();
        var categoryId = item.GetProperty("categoryId").GetGuid();
        var sub = await Expect(await admin.PostAsJsonAsync("/catalog/subcategories", new { categoryId, name = "Import subcategory" }));
        var subcategoryId = sub.GetProperty("id").GetGuid();
        await Expect(await admin.PostAsJsonAsync("/catalog/items/assign-subcategory", new { itemIds = new[] { id }, subcategoryId }));
        await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token }));
        items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        Assert(items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id).GetProperty("subcategoryId").GetGuid() == subcategoryId, "reimport preserves subcategory within category");
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = CsvImportService.Header + "\n00X,Updated,NEW,20,21" }));
        await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }));
        items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        item = items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert(item.GetProperty("subcategoryId").ValueKind == JsonValueKind.Null && item.GetProperty("price").GetDecimal() == 24.20m && item.GetProperty("name").GetString() == "Updated", "reimport updates same item and clears stale subcategory");
        var existingCategory = await Expect(await admin.PostAsJsonAsync("/catalog/categories", new { code = "Žluť", name = "Existing Unicode category", sortOrder = 42, enabled = false }));
        var unicodeCsv = CsvImportService.Header + "\nž-test-1,First,Žluť,1,12\nž-test-2,Second,žLUŤ,2,12\nž-test-3,Third,Kódy,3,12\nž-test-4,Fourth,Kódy,4,12\nž-test-5,Fifth,KÓDY,5,12";
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = unicodeCsv }));
        result = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }));
        Assert(result.GetProperty("created").GetInt32() == 5 && result.GetProperty("categoriesCreated").GetInt32() == 1, "Unicode category codes reuse existing categories and merge repeated/case-variant rows");
        items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        var unicodeItemId = items.EnumerateArray().Single(x => x.GetProperty("code").GetString() == "ž-test-1").GetProperty("id").GetGuid();
        var unicodeCategory = (await admin.GetFromJsonAsync<JsonElement>("/catalog/categories")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == existingCategory.GetProperty("id").GetGuid());
        Assert(unicodeCategory.GetProperty("name").GetString() == "Existing Unicode category" && unicodeCategory.GetProperty("sortOrder").GetInt32() == 42 && !unicodeCategory.GetProperty("enabled").GetBoolean(), "import preserves existing Unicode category metadata");
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = unicodeCsv.ToUpperInvariant() }));
        result = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }));
        items = await admin.GetFromJsonAsync<JsonElement>("/catalog/items");
        Assert(result.GetProperty("created").GetInt32() == 0 && result.GetProperty("updated").GetInt32() == 5 && result.GetProperty("categoriesCreated").GetInt32() == 0 &&
            items.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == unicodeItemId).GetProperty("name").GetString() == "FIRST", "Unicode item and category case variants reimport without duplicates");
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv }));
        result = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }));
        Assert(result.GetProperty("created").GetInt32() == 201, "full sample import");
        Assert(result.GetProperty("categoriesCreated").GetInt32() == 11, "sample category case variants merge consistently");
        result = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }));
        Assert(result.GetProperty("created").GetInt32() == 0 && result.GetProperty("updated").GetInt32() == 201, "repeat sample import updates without duplicates");
        // Deliberately ambiguous Unicode codes force a late failure after an earlier insert.
        await Expect(await admin.PostAsJsonAsync("/catalog/categories", new { code = "ž-ambiguous", name = "One" }));
        var ambiguous = await admin.PostAsJsonAsync("/catalog/categories", new { code = "Ž-AMBIGUOUS", name = "Two" });
        if ((int)ambiguous.StatusCode == 409)
        {
            Assert(true, "case-insensitive provider prevents ambiguous catalog codes");
            return;
        }
        await Expect(ambiguous);
        before = (await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength();
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = CsvImportService.Header + "\nrollback-first,First,rollback-category,1,12\nrollback-second,Second,ž-ambiguous,1,12" }));
        await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }), 409);
        Assert((await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength() == before &&
            !(await admin.GetFromJsonAsync<JsonElement>("/catalog/categories")).EnumerateArray().Any(x => x.GetProperty("code").GetString() == "rollback-category"), "late import conflict rolls back items and categories");
        var ambiguousItem = new { code = "ř-ambiguous", categoryId, name = "One", priceBeforeVat = 1, vatRate = 12 };
        await Expect(await admin.PostAsJsonAsync("/catalog/items", ambiguousItem));
        await Expect(await admin.PostAsJsonAsync("/catalog/items", new { code = "Ř-AMBIGUOUS", categoryId, name = "Two", priceBeforeVat = 1, vatRate = 12 }));
        before = (await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength();
        preview = await Expect(await admin.PostAsJsonAsync("/catalog/import/preview", new { csv = CsvImportService.Header + "\nrollback-item-first,First,rollback-item-category,1,12\nŘ-ambiguous,Second,Žluť,1,12" }));
        var conflict = await Expect(await admin.PostAsJsonAsync("/catalog/import/commit", new { token = preview.GetProperty("token").GetString() }), 409);
        Assert(conflict.GetProperty("code").GetString() == "ambiguous_import_code" &&
            (await admin.GetFromJsonAsync<JsonElement>("/catalog/items")).GetArrayLength() == before &&
            !(await admin.GetFromJsonAsync<JsonElement>("/catalog/categories")).EnumerateArray().Any(x => x.GetProperty("code").GetString() == "rollback-item-category"), "ambiguous Unicode item codes reject import and roll back earlier writes");
    }
}
