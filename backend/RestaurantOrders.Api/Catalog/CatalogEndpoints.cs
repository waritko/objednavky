using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Catalog;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var tables = app.MapGroup("/tables").RequireAuthorization();
        tables.MapGet("", async (RestaurantDbContext db, CancellationToken ct) =>
            await db.Tables.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct));
        tables.MapPost("", (TableInput input, CatalogService service, CancellationToken ct) => service.SaveTable(null, input, ct)).RequireAuthorization(p => p.RequireRole("Administrator"));
        tables.MapPut("/{id:guid}", (Guid id, TableInput input, CatalogService service, CancellationToken ct) => service.SaveTable(id, input, ct)).RequireAuthorization(p => p.RequireRole("Administrator"));

        var catalog = app.MapGroup("/catalog").RequireAuthorization();
        catalog.MapGet("/categories", async (RestaurantDbContext db, CancellationToken ct) =>
            await db.Categories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct));
        catalog.MapGet("/subcategories", async (RestaurantDbContext db, CancellationToken ct) =>
            await db.Subcategories.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct));
        catalog.MapGet("/items", async (RestaurantDbContext db, CancellationToken ct) =>
            await db.MenuItems.AsNoTracking().OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ThenBy(x => x.Id).ToListAsync(ct));

        var administration = catalog.MapGroup("").RequireAuthorization(p => p.RequireRole("Administrator"));
        administration.MapPost("/categories", (CategoryInput input, CatalogService service, CancellationToken ct) => service.SaveCategory(null, input, ct));
        administration.MapPut("/categories/{id:guid}", (Guid id, CategoryInput input, CatalogService service, CancellationToken ct) => service.SaveCategory(id, input, ct));
        administration.MapPost("/subcategories", (SubcategoryInput input, CatalogService service, CancellationToken ct) => service.SaveSubcategory(null, input, ct));
        administration.MapPut("/subcategories/{id:guid}", (Guid id, SubcategoryInput input, CatalogService service, CancellationToken ct) => service.SaveSubcategory(id, input, ct));
        administration.MapPost("/items", (ItemInput input, CatalogService service, CancellationToken ct) => service.SaveItem(null, input, ct));
        administration.MapPut("/items/{id:guid}", (Guid id, ItemInput input, CatalogService service, CancellationToken ct) => service.SaveItem(id, input, ct));
        administration.MapPost("/items/assign-subcategory", (AssignmentInput input, CatalogService service, CancellationToken ct) => service.Assign(input, ct));
    }
}
