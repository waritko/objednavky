using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Catalog;

public sealed record CsvPreviewInput(string? Csv);
public sealed record CsvCommitInput(string? Token);
public sealed record CsvRow(long Row, string Code, string Name, string CategoryCode, decimal PriceBeforeVat, decimal VatRate, decimal Price);
public sealed record CsvError(long Row, string Message);
public sealed record CsvParseResult(List<CsvRow> Rows, List<CsvError> Errors, int EmptyRows);
public sealed record CsvTicket(string Actor, DateTimeOffset ExpiresAt, List<CsvRow> Rows);

public sealed class CsvImportService(RestaurantDbContext db, IDataProtectionProvider protection)
{
    public const string Header = "CISMAT,NAZMAT,DRUMAT2,PROCEN5,SAZDPH";
    private readonly IDataProtector protector = protection.CreateProtector("RestaurantOrders.CsvImport.v1");
    private static IResult Error(string code, string message, int status = 400) => Results.Json(new { code, message }, statusCode: status);

    public static CsvParseResult Parse(string csv)
    {
        var rows = new List<CsvRow>();
        var errors = new List<CsvError>();
        var empty = 0;
        using var parser = new TextFieldParser(new StringReader(csv.TrimStart('\uFEFF')))
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = true
        };
        parser.SetDelimiters(",");
        try
        {
            if (parser.EndOfData || !parser.ReadFields()!.SequenceEqual(Header.Split(',')))
                return new(rows, [new(1, "Očekávána hlavička " + Header + ".")], empty);
            while (!parser.EndOfData)
            {
                var line = parser.LineNumber;
                string[] fields;
                try { fields = parser.ReadFields()!; }
                catch (MalformedLineException) { errors.Add(new(line, "Neplatné uvozovky nebo struktura CSV.")); continue; }
                if (fields.All(string.IsNullOrWhiteSpace)) { empty++; continue; }
                if (fields.Length != 5) { errors.Add(new(line, "Řádek musí obsahovat pět sloupců.")); continue; }
                fields = fields.Select(x => x.Trim()).ToArray();
                if (fields[0].Length is < 1 or > 50 || fields[1].Length is < 1 or > 200 || fields[2].Length is < 1 or > 50)
                { errors.Add(new(line, "Vyplňte kód položky (max. 50), název (max. 200) a kód kategorie (max. 50 znaků).")); continue; }
                const NumberStyles number = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
                if (!decimal.TryParse(fields[3], number, CultureInfo.InvariantCulture, out var price) ||
                    !decimal.TryParse(fields[4], number, CultureInfo.InvariantCulture, out var vat) || !CatalogService.ValidPrice(price, vat))
                { errors.Add(new(line, "Neplatná cena nebo DPH. Použijte desetinnou tečku, nejvýše dvě desetinná místa, nezápornou cenu a DPH 0–100.")); continue; }
                rows.Add(new(line, fields[0], fields[1], fields[2], price, vat, CatalogService.OrderingPrice(price, vat)));
            }
        }
        catch (MalformedLineException) { errors.Add(new(1, "Neplatná hlavička CSV.")); }
        // Reject every occurrence: never silently choose one of conflicting input rows.
        var duplicates = rows.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1).Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(x => duplicates.Contains(x.Code))) errors.Add(new(row.Row, "Duplicitní kód položky v souboru."));
        rows.RemoveAll(x => duplicates.Contains(x.Code));
        return new(rows, errors.OrderBy(x => x.Row).ToList(), empty);
    }

    public IResult Preview(CsvPreviewInput input, string actor)
    {
        if (input.Csv is null || input.Csv.Length > 1_000_000)
            return Error("invalid_csv", "Nahrajte CSV do velikosti 1 000 000 znaků.");
        var parsed = Parse(input.Csv);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var token = parsed.Rows.Count == 0 ? null : protector.Protect(JsonSerializer.Serialize(new CsvTicket(actor, expiresAt, parsed.Rows)));
        return Results.Ok(new { rows = parsed.Rows, errors = parsed.Errors, emptyRows = parsed.EmptyRows, token, expiresAt });
    }

    public async Task<IResult> Commit(CsvCommitInput input, string actor, CancellationToken ct)
    {
        CsvTicket? ticket;
        try { ticket = input.Token is null ? null : JsonSerializer.Deserialize<CsvTicket>(protector.Unprotect(input.Token)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException)
        { return Error("invalid_import_token", "Náhled není platný. Nahrajte soubor znovu."); }
        if (ticket is null || ticket.Actor != actor || ticket.ExpiresAt <= DateTimeOffset.UtcNow)
            return Error("invalid_import_token", "Náhled vypršel nebo patří jinému účtu. Nahrajte soubor znovu.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var created = 0;
        var updated = 0;
        var categoriesCreated = 0;
        try
        {
            foreach (var row in ticket.Rows)
            {
                // The source uses both k/K and z/Z for the same category.
                var categoryCode = row.CategoryCode.ToUpperInvariant();
                var categories = await db.Categories.Where(x => x.Code.ToUpper() == categoryCode).Take(2).ToListAsync(ct);
                if (categories.Count > 1) return Error("ambiguous_import_code", "Katalog obsahuje kódy lišící se pouze velikostí písmen. Nejprve je sjednoťte.", 409);
                var category = categories.SingleOrDefault();
                if (category is null)
                {
                    category = new Category { Code = row.CategoryCode, Name = row.CategoryCode };
                    db.Categories.Add(category);
                    await db.SaveChangesAsync(ct);
                    categoriesCreated++;
                }
                var itemCode = row.Code.ToUpperInvariant();
                var items = await db.MenuItems.Where(x => x.Code.ToUpper() == itemCode).Take(2).ToListAsync(ct);
                if (items.Count > 1) return Error("ambiguous_import_code", "Katalog obsahuje kódy lišící se pouze velikostí písmen. Nejprve je sjednoťte.", 409);
                var item = items.SingleOrDefault();
                if (item is null)
                {
                    item = new MenuItem { Code = row.Code, Name = row.Name };
                    db.MenuItems.Add(item);
                    created++;
                }
                else updated++;
                if (item.CategoryId != category.Id) item.SubcategoryId = null;
                item.CategoryId = category.Id;
                item.Name = row.Name;
                item.PriceBeforeVat = row.PriceBeforeVat;
                item.VatRate = row.VatRate;
                item.Price = row.Price;
                await db.SaveChangesAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        { return Error("import_conflict", "Katalog byl souběžně změněn. Import nebyl uložen; zkuste jej znovu.", 409); }
        return Results.Ok(new { created, updated, categoriesCreated });
    }
}
