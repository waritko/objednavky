using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;

internal static class HistoryChecks
{
    public static async Task Run(HttpClient staff, TestDatabase database)
    {
        await using var db = database.Context();
        var actor = await db.Accounts.Select(x => x.Id).FirstAsync();
        var item = await db.MenuItems.FirstAsync();
        var table = new RestaurantTable { Name = "History sorting" };
        db.Add(table);
        RestaurantOrder Order(string id, string closed, params string?[] payments)
        {
            var order = new RestaurantOrder
            {
                Id = Guid.Parse(id),
                TableId = table.Id,
                CreatedByAccountId = actor,
                OpenedAt = DateTimeOffset.Parse("2026-10-01T08:00:00Z"),
                ClosedAt = DateTimeOffset.Parse(closed),
                State = OrderState.Closed
            };
            foreach (var paid in payments)
                order.Units.Add(new OrderUnit
                {
                    MenuItemId = item.Id,
                    ItemName = item.Name,
                    CategoryName = "History",
                    AddedAt = order.OpenedAt,
                    AddedByAccountId = actor,
                    UnitPrice = 10,
                    PaidAt = paid is null ? null : DateTimeOffset.Parse(paid),
                    RemovedAt = paid is null ? order.ClosedAt : null
                });
            db.Add(order);
            return order;
        }
        var older = Order("00000000-0000-0000-0000-000000000001", "2026-10-03T12:00:00Z", "2026-10-02T12:00:00Z");
        var newer = Order("00000000-0000-0000-0000-000000000002", "2026-10-02T13:00:00Z",
            "2026-10-01T12:00:00Z", "2026-10-02T14:30:00+02:00");
        var unpaid = Order("00000000-0000-0000-0000-000000000003", "2026-10-04T12:00:00Z", (string?)null);
        await db.SaveChangesAsync();

        async Task<JsonElement> Page(int number) => await staff.GetFromJsonAsync<JsonElement>(
            $"/orders/history?tableId={table.Id}&pageSize=1&page={number}");
        var expected = new[] { newer.Id, older.Id, unpaid.Id };
        for (var index = 0; index < expected.Length; index++)
        {
            var page = await Page(index + 1);
            if (page.GetProperty("total").GetInt32() != 3 ||
                page.GetProperty("orders")[0].GetProperty("id").GetGuid() != expected[index])
                throw new Exception("History must sort by latest payment instant before pagination, with unpaid orders last");
        }
        if ((await Page(4)).GetProperty("orders").GetArrayLength() != 0)
            throw new Exception("History page past the end must be empty");
        Console.WriteLine("PASS history latest payment ordering, time offsets, unpaid orders and pagination");
    }
}
