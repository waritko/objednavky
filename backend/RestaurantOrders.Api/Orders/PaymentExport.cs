using Microsoft.EntityFrameworkCore;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Persistence;

namespace RestaurantOrders.Api.Orders;

// VYDEJ belongs to the existing inventory system; application migrations never manage it.
public sealed class PaymentExport(RestaurantDbContext db, IConfiguration configuration)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");

    public async Task Export(int orderId, OrderUnit[] units, DateTimeOffset paidAt, CancellationToken ct)
    {
        if (units.Length == 0 || !configuration.GetValue("PaymentExport:Enabled", db.Database.IsSqlServer()))
            return;

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Payment export requires the order transaction.");

        // Update/range locks serialize number allocation across payments and API instances.
        var number = db.Database.IsSqlServer()
            ? await db.Database.SqlQueryRaw<int>("SELECT COALESCE(MAX(cisvyd), 0) + 1 AS [Value] FROM dbo.VYDEJ WITH (UPDLOCK, HOLDLOCK) WHERE sklad = 1 AND cisrad = 0").SingleAsync(ct)
            : await db.Database.SqlQueryRaw<int>("SELECT COALESCE(MAX(cisvyd), 0) + 1 AS Value FROM VYDEJ WHERE sklad = 1 AND cisrad = 0").SingleAsync(ct);
        var date = TimeZoneInfo.ConvertTime(paidAt, Zone).DateTime;

        // Keep different historical prices/tax rates separate so jedcen and procen remain exact.
        foreach (var line in units.GroupBy(x => new { x.ItemCode, x.CategoryCode, x.ItemName, x.UnitPriceBeforeVat, x.VatRate }))
        {
            var count = line.Count();
            var total = line.Key.UnitPriceBeforeVat * count;
            if (db.Database.IsSqlServer())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.VYDEJ
                        (sklad, cisrad, cisvyd, datvyd, cisobj, drumat1, drumat2, cismat, nazmat,
                         um, mnozst, ucdkl, str, zak, procen, malvel, sazdph, jedcen, znamen)
                    VALUES (1, 0, {number}, {date}, {orderId}, 'I', {line.Key.CategoryCode},
                        {line.Key.ItemCode}, {line.Key.ItemName}, {date.Month}, {count}, 'N', 'P', 'K',
                        {total}, 'V', {line.Key.VatRate}, {line.Key.UnitPriceBeforeVat}, {"Kč"})
                    """, ct);
            else
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO VYDEJ
                        (sklad, cisrad, cisvyd, datvyd, cisobj, drumat1, drumat2, cismat, nazmat,
                         um, mnozst, ucdkl, str, zak, procen, malvel, sazdph, jedcen, znamen)
                    VALUES (1, 0, {number}, {date}, {orderId}, 'I', {line.Key.CategoryCode},
                        {line.Key.ItemCode}, {line.Key.ItemName}, {date.Month}, {count}, 'N', 'P', 'K',
                        {total}, 'V', {line.Key.VatRate}, {line.Key.UnitPriceBeforeVat}, {"Kč"})
                    """, ct);
        }
    }
}
