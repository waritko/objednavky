using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Orders;

internal static class SalesChecks
{
    public static async Task Run(HttpClient admin, HttpClient staff, HttpClient anonymous, TestDatabase database)
    {
        foreach (var (client, status) in new[] { (anonymous, 401), (staff, 403), (admin, 200) })
        {
            using var response = await client.GetAsync("/orders/sales");
            if ((int)response.StatusCode != status) throw new Exception($"Sales authorization expected {status}, got {response.StatusCode}");
        }
        var responseReport = await admin.GetFromJsonAsync<JsonElement>("/orders/sales");
        if (responseReport.GetProperty("days").GetArrayLength() != 7) throw new Exception("Sales must return seven days");
        await using var db = database.Context();
        var actor = await db.Accounts.Select(x => x.Id).FirstAsync();
        var table = new RestaurantTable { Name = "Sales test" };
        var category = new Category { Code = "SALES", Name = "Sales" };
        var item = new MenuItem { Code = "SALES", Name = "Renamed item", CategoryId = category.Id, Price = 999 };
        var order = new RestaurantOrder { TableId = table.Id, ActiveTableId = table.Id, CreatedByAccountId = actor, OpenedAt = DateTimeOffset.Parse("2026-03-01T00:00:00Z") };
        var closed = new RestaurantOrder { TableId = table.Id, CreatedByAccountId = actor, OpenedAt = order.OpenedAt, State = OrderState.Closed };
        db.AddRange(table, category, item, order, closed);
        void Unit(string? paid, decimal price = 10, bool removed = false, bool isClosed = false)
        {
            db.OrderUnits.Add(new OrderUnit
            {
                OrderId = isClosed ? closed.Id : order.Id,
                MenuItemId = item.Id,
                ItemName = "Original item",
                CategoryName = category.Name,
                UnitPrice = price,
                AddedAt = order.OpenedAt,
                AddedByAccountId = actor,
                PaidAt = paid is null ? null : DateTimeOffset.Parse(paid),
                RemovedAt = removed ? DateTimeOffset.Parse("2026-03-29T08:00:00Z") : null
            });
        }
        // The seven-day window crosses Prague's transition to summer time.
        Unit("2026-03-22T22:59:59Z", 1000); // just before first day
        Unit("2026-03-22T22:59:59.9999999Z", 1000);
        Unit("2026-03-22T23:00:00Z", 11); // first day at local midnight
        Unit("2026-03-28T23:00:00Z", 12); // today at local midnight
        Unit("2026-03-29T03:00:00+02:00", 13, removed: true);
        Unit("2026-03-29T21:59:59Z", 14, isClosed: true);
        Unit("2026-03-29T21:59:59.9999999Z", 15);
        Unit("2026-03-29T22:00:00Z", 1000); // tomorrow at local midnight
        Unit(null, 1000);
        await db.SaveChangesAsync();
        var report = await SalesReports.Read(db, DateTimeOffset.Parse("2026-03-29T20:00:00Z"), CancellationToken.None);
        if (report.Days.Length != 7 || report.Days[0].Date != new DateOnly(2026, 3, 29) || report.Days[6].Date != new DateOnly(2026, 3, 23))
            throw new Exception("Incorrect Prague sales window");
        if (report.Days[0].Quantity != 4 || report.Days[0].Total != 54 || report.Days[0].Items.Single().ItemName != "Original item")
            throw new Exception("Sales must include paid active, closed and removed units at historical prices and names");
        if (report.Days[6].Total != 11 || report.Days.Skip(1).Take(5).Any(x => x.Quantity != 0 || x.Total != 0 || x.Items.Length != 0))
            throw new Exception("Sales boundaries or empty days are incorrect");
        Console.WriteLine("PASS sales authorization, seven Prague days, DST boundaries, paid-only totals and historical snapshots");
    }
}
