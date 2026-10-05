using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Orders;

public sealed record AddUnitInput(Guid MenuItemId, Guid UnitId);
public sealed record ChangeUnitsInput(Guid ConcurrencyToken, Guid[]? UnitIds, bool All = false, bool ConfirmPaidRemoval = false);

public sealed class OrderService(RestaurantDbContext db)
{
    public static object View(RestaurantOrder order)
    {
        var current = order.Units.Where(x => x.RemovedAt is null).ToArray();
        return new { order.Id, order.TableId, order.OpenedAt, order.ClosedAt, state = order.State.ToString(),
            order.CreatedByAccountId, order.ConcurrencyToken, total = current.Sum(x => x.UnitPrice),
            paid = current.Where(x => x.PaidAt is not null).Sum(x => x.UnitPrice),
            unpaid = current.Where(x => x.PaidAt is null).Sum(x => x.UnitPrice),
            undeliveredCount = current.Count(x => x.ProcessedAt is null),
            unpaidCount = current.Count(x => x.PaidAt is null), units = order.Units.OrderBy(x => x.AddedAt).ThenBy(x => x.Id) };
    }

    private static IResult Error(string code, string message, int status = 400) => Results.Json(new { code, message }, statusCode: status);
    private static IResult Conflict() => Error("order_conflict", "Objednávka se změnila. Obnovte ji a opakujte akci.", 409);

    private async Task<IResult> Write(Func<Task<IResult>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var result = await action();
            if (result is not Microsoft.AspNetCore.Http.IStatusCodeHttpResult { StatusCode: >= 400 })
            {
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            return result;
        }
        catch (DbUpdateException) { return Conflict(); }
        catch (SqliteException e) when (e.SqliteErrorCode is 5 or 6 or 19) { return Conflict(); }
        catch (SqlException e) when (e.Number is 1205 or 2601 or 2627) { return Conflict(); }
    }

    public Task<IResult> Add(Guid tableId, AddUnitInput input, Guid actor, CancellationToken ct) => Write(async () =>
    {
        if (input.UnitId == Guid.Empty) return Error("invalid_unit_id", "Chybí identifikátor přidání položky.");
        var existing = await db.OrderUnits.AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.UnitId, ct);
        if (existing is not null)
        {
            var original = await db.Orders.Include(x => x.Units).SingleAsync(x => x.Id == existing.OrderId, ct);
            return original.TableId == tableId && existing.MenuItemId == input.MenuItemId && existing.AddedByAccountId == actor
                ? Results.Ok(View(original)) : Conflict();
        }
        if (!await db.Tables.AnyAsync(x => x.Id == tableId && x.Enabled, ct))
            return Error("table_unavailable", "Stůl není dostupný.");
        var item = await db.MenuItems.SingleOrDefaultAsync(x => x.Id == input.MenuItemId && x.Enabled, ct);
        if (item is null) return Error("item_unavailable", "Položka není dostupná.");
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == item.CategoryId && x.Enabled, ct);
        var subcategory = item.SubcategoryId is null ? null : await db.Subcategories.SingleOrDefaultAsync(x => x.Id == item.SubcategoryId && x.Enabled && x.CategoryId == item.CategoryId, ct);
        if (category is null || (item.SubcategoryId is not null && subcategory is null))
            return Error("item_unavailable", "Kategorie položky není dostupná.");
        var now = DateTimeOffset.UtcNow;
        var order = await db.Orders.Include(x => x.Units).SingleOrDefaultAsync(x => x.ActiveTableId == tableId, ct);
        if (order is null)
        {
            order = new RestaurantOrder { TableId = tableId, ActiveTableId = tableId, CreatedByAccountId = actor, OpenedAt = now };
            db.Orders.Add(order);
        }
        var unit = new OrderUnit { Id = input.UnitId, OrderId = order.Id, MenuItemId = item.Id, ItemName = item.Name,
            CategoryName = category.Name, SubcategoryName = subcategory?.Name, UnitPrice = item.Price, AddedAt = now, AddedByAccountId = actor };
        order.Units.Add(unit);
        // Explicitly mark a client-assigned key as new on existing orders.
        db.OrderUnits.Add(unit);
        order.ConcurrencyToken = Guid.NewGuid();
        Audit(order, unit, actor, now, "Added", new { unit.ItemName, unit.UnitPrice });
        return Results.Ok(View(order));
    }, ct);

    public Task<IResult> Change(Guid id, string action, ChangeUnitsInput input, Guid actor, CancellationToken ct) => Write(async () =>
    {
        if (action is not ("processed" or "paid" or "removed")) return Results.NotFound();
        if ((input.All && input.UnitIds is { Length: > 0 }) || (!input.All && input.UnitIds is not { Length: > 0 }))
            return Error("invalid_selection", "Vyberte položky nebo celou objednávku.");
        var order = await db.Orders.Include(x => x.Units).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null) return Results.NotFound();
        if (order.State == OrderState.Closed) return Error("order_closed", "Uzavřenou objednávku nelze změnit.", 409);
        if (order.ConcurrencyToken != input.ConcurrencyToken) return Conflict();
        if (input.UnitIds?.Any(id => order.Units.All(x => x.Id != id)) == true)
            return Error("invalid_selection", "Položka nepatří do objednávky.");
        var selected = order.Units.Where(x => x.RemovedAt is null && (input.All || input.UnitIds!.Contains(x.Id))).ToArray();
        if (action == "removed" && selected.Any(x => x.PaidAt is not null) && !input.ConfirmPaidRemoval)
            return Error("paid_removal_confirmation", "Potvrďte odebrání zaplacené položky. Vrácení platby se neprovádí.");
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var unit in selected)
        {
            var before = new { unit.PaidAt, unit.ProcessedAt, unit.RemovedAt, unit.UnitPrice };
            switch (action)
            {
                case "processed" when unit.ProcessedAt is null: unit.ProcessedAt = now; unit.ProcessedByAccountId = actor; break;
                case "paid" when unit.PaidAt is null: unit.PaidAt = now; unit.PaidByAccountId = actor; break;
                case "removed": unit.RemovedAt = now; unit.RemovedByAccountId = actor; break;
                default: continue;
            }
            changed = true;
            Audit(order, unit, actor, now, action, new { before, after = new { unit.PaidAt, unit.ProcessedAt, unit.RemovedAt, unit.UnitPrice } });
        }
        if (changed)
        {
            order.ConcurrencyToken = Guid.NewGuid();
            if (order.Units.All(x => x.RemovedAt is not null || (x.PaidAt is not null && x.ProcessedAt is not null)))
            {
                order.State = OrderState.Closed;
                order.ClosedAt = now;
                order.ActiveTableId = null;
                Audit(order, null, actor, now, "Closed", new { });
            }
        }
        return Results.Ok(View(order));
    }, ct);

    private void Audit(RestaurantOrder order, OrderUnit? unit, Guid actor, DateTimeOffset now, string action, object details) =>
        db.AuditEvents.Add(new AuditEvent { OrderId = order.Id, UnitId = unit?.Id, ActorAccountId = actor,
            OccurredAt = now, Action = action, DetailsJson = JsonSerializer.Serialize(details) });
}
