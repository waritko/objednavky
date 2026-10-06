using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Orders;

public sealed record SoldItem(Guid MenuItemId, string ItemName, int Quantity, decimal Total);
public sealed record SalesDay(DateOnly Date, int Quantity, decimal Total, SoldItem[] Items);
public sealed record SalesReport(string TimeZone, SalesDay[] Days);

public static class SalesReports
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public static async Task<SalesReport> Read(RestaurantDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Zone).DateTime);
        var first = today.AddDays(-6);
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(first.ToDateTime(TimeOnly.MinValue), Zone));
        var end = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(today.AddDays(1).ToDateTime(TimeOnly.MinValue), Zone));
        // SQLite cannot compare DateTimeOffset values through LINQ. julianday normalizes
        // the stored offsets. Widen by one second to account for SQLite date rounding;
        // apply the exact boundaries below before grouping.
        var query = db.Database.IsSqlite()
            ? db.OrderUnits.FromSqlInterpolated($"SELECT * FROM OrderUnits WHERE PaidAt IS NOT NULL AND julianday(PaidAt) >= julianday({start.AddSeconds(-1).ToString("O")}) AND julianday(PaidAt) < julianday({end.AddSeconds(1).ToString("O")})")
            : db.OrderUnits.Where(x => x.PaidAt >= start && x.PaidAt < end);
        var units = await query.AsNoTracking()
            .Select(x => new { x.MenuItemId, x.ItemName, x.UnitPrice, x.PaidAt }).ToListAsync(ct);
        var byDay = units.Where(x => x.PaidAt >= start && x.PaidAt < end)
            .ToLookup(x => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.PaidAt!.Value, Zone).DateTime));
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = today.AddDays(-offset);
            var items = byDay[date].GroupBy(x => new { x.MenuItemId, x.ItemName })
                .Select(group => new SoldItem(group.Key.MenuItemId, group.Key.ItemName, group.Count(), group.Sum(x => x.UnitPrice)))
                .OrderBy(x => x.ItemName).ThenBy(x => x.MenuItemId).ToArray();
            return new SalesDay(date, items.Sum(x => x.Quantity), items.Sum(x => x.Total), items);
        }).ToArray();
        return new SalesReport(Zone.Id, days);
    }
}
