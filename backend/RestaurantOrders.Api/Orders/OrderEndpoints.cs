using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;
using RestaurantOrders.Api.Domain;

namespace RestaurantOrders.Api.Orders;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/orders").RequireAuthorization();
        orders.MapGet("/sales", (RestaurantDbContext db, CancellationToken ct) =>
            SalesReports.Read(db, DateTimeOffset.UtcNow, ct))
            .RequireAuthorization(p => p.RequireRole("Administrator"));
        orders.MapGet("", async (bool? undelivered, bool? unpaid, RestaurantDbContext db, CancellationToken ct) =>
        {
            var query = db.Orders.AsNoTracking().Where(x => x.State == OrderState.Active);
            if (undelivered == true) query = query.Where(x => x.Units.Any(u => u.RemovedAt == null && u.ProcessedAt == null));
            if (unpaid == true) query = query.Where(x => x.Units.Any(u => u.RemovedAt == null && u.PaidAt == null));
            return Results.Ok((await query.Include(x => x.Units).ToListAsync(ct)).OrderBy(x => x.OpenedAt).Select(OrderService.View));
        });
        orders.MapGet("/history", async (int? page, int? pageSize, Guid? tableId, RestaurantDbContext db, CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 25;
            if (number < 1 || number > 1000000 || size < 1 || size > 100)
                return Results.BadRequest(new { code = "invalid_page", message = "Neplatná stránka historie." });
            var query = db.Orders.AsNoTracking().Where(x => x.State == OrderState.Closed);
            if (tableId.HasValue) query = query.Where(x => x.TableId == tableId.Value);
            var total = await query.CountAsync(ct);
            // Sort timestamps in memory because SQLite cannot order DateTimeOffset values.
            // Load only payment dates for sorting, then fetch the requested page's full orders.
            var dates = await query.Select(x => new
            {
                x.Id,
                x.ClosedAt,
                PaidAt = x.Units.Select(u => u.PaidAt).ToList()
            }).ToListAsync(ct);
            var ids = dates.OrderByDescending(x => x.PaidAt.Max())
                .ThenByDescending(x => x.ClosedAt).ThenBy(x => x.Id)
                .Skip((number - 1) * size).Take(size).Select(x => x.Id).ToArray();
            var rows = await query.Where(x => ids.Contains(x.Id)).Include(x => x.Units).ToDictionaryAsync(x => x.Id, ct);
            return Results.Ok(new { page = number, pageSize = size, total, orders = ids.Select(id => OrderService.View(rows[id])) });
        });
        orders.MapGet("/{id:guid}/audit", async (Guid id, RestaurantDbContext db, CancellationToken ct) =>
        {
            if (!await db.Orders.AnyAsync(x => x.Id == id, ct)) return Results.NotFound();
            var events = await (from entry in db.AuditEvents.AsNoTracking()
                                join actor in db.Accounts on entry.ActorAccountId equals actor.Id
                                where entry.OrderId == id
                                select new { entry.Id, entry.UnitId, entry.Action, entry.ActorAccountId, actor.Username, entry.OccurredAt, entry.DetailsJson }).ToListAsync(ct);
            return Results.Ok(events.OrderBy(x => x.OccurredAt).ThenBy(x => x.Id));
        });
        orders.MapGet("/{id:guid}", async (Guid id, RestaurantDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.Units).SingleOrDefaultAsync(x => x.Id == id, ct);
            return order is null ? Results.NotFound() : Results.Ok(OrderService.View(order));
        });
        app.MapGet("/tables/{id:guid}/order", async (Guid id, RestaurantDbContext db, CancellationToken ct) =>
        {
            var order = await db.Orders.AsNoTracking().Include(x => x.Units).SingleOrDefaultAsync(x => x.ActiveTableId == id, ct);
            return order is null ? Results.NoContent() : Results.Ok(OrderService.View(order));
        }).RequireAuthorization();
        app.MapPost("/tables/{id:guid}/units", (Guid id, AddUnitInput input, OrderService service, ClaimsPrincipal user, CancellationToken ct) =>
            service.Add(id, input, Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct)).RequireAuthorization();
        orders.MapPost("/{id:guid}/note", (Guid id, ChangeNoteInput input, OrderService service, ClaimsPrincipal user, CancellationToken ct) =>
            service.ChangeNote(id, input, Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct));
        orders.MapPost("/{id:guid}/{action}", (Guid id, string action, ChangeUnitsInput input, OrderService service, ClaimsPrincipal user, CancellationToken ct) =>
            service.Change(id, action, input, Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct));
    }
}
