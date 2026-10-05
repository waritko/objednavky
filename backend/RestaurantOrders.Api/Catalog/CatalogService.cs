using System.Data;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Catalog;

public sealed record TableInput(string? Name, int SortOrder = 0, bool Enabled = true);
public sealed record CategoryInput(string? Code, string? Name, int SortOrder = 0, bool Enabled = true);
public sealed record SubcategoryInput(Guid CategoryId, string? Name, int SortOrder = 0, bool Enabled = true);
public sealed record ItemInput(string? Code, Guid CategoryId, Guid? SubcategoryId, string? Name,
    decimal PriceBeforeVat, decimal VatRate, int SortOrder = 0, bool Enabled = true);
public sealed record AssignmentInput(Guid[]? ItemIds, Guid? SubcategoryId);

public sealed class CatalogService(RestaurantDbContext db)
{
    public static decimal OrderingPrice(decimal priceBeforeVat, decimal vatRate) =>
        decimal.Round(priceBeforeVat * (1 + vatRate / 100), 2, MidpointRounding.AwayFromZero);

    private static bool Text(string? value, int maximum) => value?.Trim().Length is > 0 && value.Trim().Length <= maximum;
    private static IResult Error(string code, string message, int status = 400) => Results.Json(new { code, message }, statusCode: status);
    private static IResult Invalid() => Error("invalid_catalog_input", "Vyplňte platný název a kód v povolené délce.");

    // Serialize relationship validation with catalog writes on either database provider.
    private async Task<IResult> Write(Func<Task<IResult>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var result = await action();
        if (result is not Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: >= 400 })
        {
            try
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Error("catalog_conflict", "Záznam byl souběžně změněn nebo kód již existuje. Obnovte seznam a zkuste to znovu.", 409);
            }
        }
        return result;
    }

    public Task<IResult> SaveTable(Guid? id, TableInput input, CancellationToken ct) => Write(async () =>
    {
        if (!Text(input.Name, 100)) return Invalid();
        var entity = id is null ? new RestaurantTable { Name = "" } : await db.Tables.FindAsync([id.Value], ct);
        if (entity is null) return Results.NotFound();
        entity.Name = input.Name!.Trim();
        entity.SortOrder = input.SortOrder;
        entity.Enabled = input.Enabled;
        if (id is null) db.Tables.Add(entity);
        return Results.Ok(entity);
    }, ct);

    public Task<IResult> SaveCategory(Guid? id, CategoryInput input, CancellationToken ct) => Write(async () =>
    {
        if (!Text(input.Name, 150) || !Text(input.Code, 50)) return Invalid();
        var entity = id is null ? new Category { Name = "", Code = "" } : await db.Categories.FindAsync([id.Value], ct);
        if (entity is null) return Results.NotFound();
        var code = input.Code!.Trim();
        if (await db.Categories.AnyAsync(x => x.Code == code && x.Id != entity.Id, ct))
            return Error("code_exists", "Kód již existuje.", 409);
        entity.Name = input.Name!.Trim();
        entity.Code = code;
        entity.SortOrder = input.SortOrder;
        entity.Enabled = input.Enabled;
        if (id is null) db.Categories.Add(entity);
        return Results.Ok(entity);
    }, ct);

    public Task<IResult> SaveSubcategory(Guid? id, SubcategoryInput input, CancellationToken ct) => Write(async () =>
    {
        if (!Text(input.Name, 150)) return Invalid();
        var entity = id is null ? new Subcategory { Name = "" } : await db.Subcategories.FindAsync([id.Value], ct);
        if (entity is null) return Results.NotFound();
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId, ct))
            return Error("invalid_category", "Kategorie neexistuje.");
        if (id is not null && entity.CategoryId != input.CategoryId && await db.MenuItems.AnyAsync(x => x.SubcategoryId == entity.Id, ct))
            return Error("subcategory_in_use", "Nejprve odeberte přiřazení položek k této podkategorii.", 409);
        entity.Name = input.Name!.Trim();
        entity.CategoryId = input.CategoryId;
        entity.SortOrder = input.SortOrder;
        entity.Enabled = input.Enabled;
        if (id is null) db.Subcategories.Add(entity);
        return Results.Ok(entity);
    }, ct);

    public Task<IResult> SaveItem(Guid? id, ItemInput input, CancellationToken ct) => Write(async () =>
    {
        if (!Text(input.Name, 200) || !Text(input.Code, 50)) return Invalid();
        // Bound arithmetic and enforce the same decimal scale on SQLite and SQL Server.
        if (input.PriceBeforeVat < 0 || input.PriceBeforeVat > 4999999999999999.99m ||
            decimal.Round(input.PriceBeforeVat, 2) != input.PriceBeforeVat ||
            input.VatRate < 0 || input.VatRate > 100 || decimal.Round(input.VatRate, 2) != input.VatRate)
            return Error("invalid_price", "Cena musí být nezáporná, DPH v rozsahu 0–100 a obě hodnoty nejvýše se dvěma desetinnými místy.");
        var entity = id is null ? new MenuItem { Name = "", Code = "" } : await db.MenuItems.FindAsync([id.Value], ct);
        if (entity is null) return Results.NotFound();
        var code = input.Code!.Trim();
        if (await db.MenuItems.AnyAsync(x => x.Code == code && x.Id != entity.Id, ct))
            return Error("code_exists", "Kód již existuje.", 409);
        if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId, ct))
            return Error("invalid_category", "Kategorie neexistuje.");
        if (input.SubcategoryId is not null && !await db.Subcategories.AnyAsync(x => x.Id == input.SubcategoryId && x.CategoryId == input.CategoryId, ct))
            return Error("invalid_subcategory", "Podkategorie nepatří do zvolené kategorie.");
        entity.Code = code;
        entity.Name = input.Name!.Trim();
        entity.CategoryId = input.CategoryId;
        entity.SubcategoryId = input.SubcategoryId;
        entity.PriceBeforeVat = input.PriceBeforeVat;
        entity.VatRate = input.VatRate;
        entity.Price = OrderingPrice(input.PriceBeforeVat, input.VatRate);
        entity.SortOrder = input.SortOrder;
        entity.Enabled = input.Enabled;
        if (id is null) db.MenuItems.Add(entity);
        return Results.Ok(entity);
    }, ct);

    public Task<IResult> Assign(AssignmentInput input, CancellationToken ct) => Write(async () =>
    {
        if (input.ItemIds is null || input.ItemIds.Length is < 1 or > 500)
            return Error("invalid_selection", "Vyberte 1–500 položek.");
        var ids = input.ItemIds.Distinct().ToArray();
        var items = await db.MenuItems.Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (items.Count != ids.Length) return Results.NotFound();
        if (input.SubcategoryId is not null)
        {
            var subcategory = await db.Subcategories.FindAsync([input.SubcategoryId.Value], ct);
            if (subcategory is null || items.Any(x => x.CategoryId != subcategory.CategoryId))
                return Error("invalid_subcategory", "Všechny položky musí patřit do kategorie zvolené podkategorie.");
        }
        foreach (var item in items) item.SubcategoryId = input.SubcategoryId;
        return Results.Ok(items);
    }, ct);
}
