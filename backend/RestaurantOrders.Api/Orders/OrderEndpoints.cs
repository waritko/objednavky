using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Orders;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        var orders = app.MapGroup("/orders").RequireAuthorization();
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
        orders.MapPost("/{id:guid}/{action}", (Guid id, string action, ChangeUnitsInput input, OrderService service, ClaimsPrincipal user, CancellationToken ct) =>
            service.Change(id, action, input, Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!), ct));
    }
}
