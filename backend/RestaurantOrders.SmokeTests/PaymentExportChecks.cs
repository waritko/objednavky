using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RestaurantOrders.Api.Domain;
using RestaurantOrders.Api.Orders;

internal static class PaymentExportChecks
{
    public static async Task Run(bool sqlServer)
    {
        await using var database = new TestDatabase(sqlServer);
        await database.Initialize();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PaymentExport:Enabled"] = "true"
        }).Build();
        await using var setup = database.Context();
        // An external fixture, never part of application migrations. SQL widths match dbo.VYDEJ.
        await setup.Database.ExecuteSqlRawAsync("""
            CREATE TABLE VYDEJ (
                sklad smallint NOT NULL, cisrad tinyint NOT NULL, cisvyd int NOT NULL,
                datvyd datetime NOT NULL, cisobj int NOT NULL, drumat1 char(5) NOT NULL,
                drumat2 char(2) NOT NULL, cismat char(15) NOT NULL CHECK (cismat <> 'FAIL'),
                nazmat varchar(30) NOT NULL, um int NOT NULL, mnozst numeric(12,3) NOT NULL,
                ucdkl char(1) NOT NULL, str varchar(6) NOT NULL, zak varchar(10) NOT NULL,
                procen numeric(14,2) NOT NULL, malvel char(1) NOT NULL,
                sazdph numeric(3,1) NOT NULL, jedcen numeric(12,3) NOT NULL, znamen nvarchar(2) NOT NULL)
            """);
        await setup.Database.ExecuteSqlRawAsync("""
            INSERT INTO VYDEJ VALUES (1,0,40,'2026-01-01',0,'I','01','seed','seed',1,1,'N','P','K',1,'V',12,1,'');
            INSERT INTO VYDEJ VALUES (1,1,900,'2026-01-01',0,'I','01','seed','seed',1,1,'N','P','K',1,'V',12,1,'');
            INSERT INTO VYDEJ VALUES (2,0,800,'2026-01-01',0,'I','01','seed','seed',1,1,'N','P','K',1,'V',12,1,'')
            """);
        var actor = new Account { Username = "export", PasswordHash = "unused" };
        var table = new RestaurantTable { Name = "Export" };
        var category = new Category { Code = "01", Name = "Category" };
        setup.AddRange(actor, table, category);
        await setup.SaveChangesAsync();
        var item = new MenuItem { Code = "000012", CategoryId = category.Id, Name = "Original", PriceBeforeVat = 100.25m, VatRate = 12m, Price = 112.28m };
        var other = new MenuItem { Code = "000013", CategoryId = category.Id, Name = "Other", PriceBeforeVat = 20m, VatRate = 21m, Price = 24.2m };
        var bad = new MenuItem { Code = "FAIL", CategoryId = category.Id, Name = "Failure", PriceBeforeVat = 1m, VatRate = 0m, Price = 1m };
        setup.AddRange(item, other, bad);
        await setup.SaveChangesAsync();

        static void Assert(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine($"PASS {name}");
        }
        async Task Add(int itemId)
        {
            await using var db = database.Context();
            var service = new OrderService(db, new PaymentExport(db, configuration));
            var result = await service.Add(table.Id, new AddUnitInput(itemId, Guid.NewGuid()), actor.Id, default);
            Assert(result is IStatusCodeHttpResult { StatusCode: 200 }, "export order unit added");
        }
        async Task<RestaurantOrder> Order()
        {
            await using var db = database.Context();
            return await db.Orders.AsNoTracking().Include(x => x.Units).SingleAsync();
        }
        async Task<int?> Change(RestaurantOrder order, string action, int[]? ids = null, bool all = false, Guid? token = null)
        {
            await using var db = database.Context();
            var result = await new OrderService(db, new PaymentExport(db, configuration)).Change(order.Id, action,
                new ChangeUnitsInput(token ?? order.ConcurrencyToken, ids, all), actor.Id, default);
            return ((IStatusCodeHttpResult)result).StatusCode;
        }
        async Task<List<ExportRow>> Rows()
        {
            await using var db = database.Context();
            return await db.Database.SqlQueryRaw<ExportRow>("SELECT CAST(sklad AS int) AS sklad, CAST(cisrad AS int) AS cisrad, cisvyd, datvyd, cisobj, drumat1, drumat2, cismat, nazmat, um, mnozst, ucdkl, str, zak, procen, malvel, sazdph, jedcen, znamen FROM VYDEJ WHERE cisobj > 0 ORDER BY cisvyd, cismat, jedcen").ToListAsync();
        }

        await Add(item.Id);
        await Add(item.Id);
        await Add(item.Id);
        await Add(other.Id);
        var order = await Order();
        var first = order.Units.Where(x => x.MenuItemId == item.Id).Take(2).Select(x => x.Id).ToArray();
        // Catalog changes after adding units must not alter exported code, name, category or tax.
        item.Code = "CHANGED";
        item.Name = "Changed";
        item.PriceBeforeVat = 999m;
        item.VatRate = 21m;
        item.Price = 1208.79m;
        category.Code = "02";
        await setup.SaveChangesAsync();
        Assert(await Change(order, "paid", first) == 200, "partial payment exported");
        var rows = await Rows();
        var row = rows.Single();
        var paidOrder = await Order();
        var paymentTime = TimeZoneInfo.ConvertTime(paidOrder.Units.First(x => first.Contains(x.Id)).PaidAt!.Value,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")).DateTime;
        Assert(row.cisvyd == 41 && row.cisobj == order.Id && row.sklad == 1 && row.cisrad == 0 &&
            Math.Abs((row.datvyd - paymentTime).TotalMilliseconds) < 4 && row.um == paymentTime.Month && row.drumat1.Trim() == "I" && row.drumat2.Trim() == "01" &&
            row.cismat.Trim() == "000012" && row.nazmat == "Original" && row.mnozst == 2 && row.procen == 200.5m &&
            row.jedcen == 100.25m && row.sazdph == 12 && row.ucdkl == "N" && row.str == "P" && row.zak == "K" && row.malvel == "V" && row.znamen == "Kč",
            "VYDEJ mapping, grouped count, historical snapshots, net amounts and filtered numbering");
        Assert(await Change(order, "paid", first) == 409 && (await Rows()).Count == 1, "stale payment does not duplicate export");
        order = await Order();
        Assert(await Change(order, "paid", first) == 200 && (await Rows()).Count == 1, "already paid units create no export");
        Assert(await Change(order, "paid", all: true) == 200, "remaining whole-order payment exported");
        rows = await Rows();
        Assert(rows.Count == 3 && rows.Count(x => x.cisvyd == 42) == 2 && rows.Where(x => x.cisvyd == 42).Sum(x => x.mnozst) == 2,
            "one shared document number per payment, only newly paid quantities");
        paidOrder = await Order();
        paymentTime = TimeZoneInfo.ConvertTime(paidOrder.Units.First(x => !first.Contains(x.Id)).PaidAt!.Value,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague")).DateTime;
        // SQL Server datetime rounds to approximately 3.33 ms; SQLite preserves the timestamp.
        Assert(rows.All(x => x.znamen == "Kč") && rows.Where(x => x.cisvyd == 42)
            .All(x => Math.Abs((x.datvyd - paymentTime).TotalMilliseconds) < 4),
            "whole-order payment exports Kč and the payment date and time");
        await Add(item.Id);
        order = await Order();
        var remove = order.Units.Single(x => x.PaidAt is null).Id;
        Assert(await Change(order, "removed", [remove]) == 200, "unpaid removal accepted");
        order = await Order();
        Assert(await Change(order, "paid", all: true) == 200 && (await Rows()).Count == 3, "removed units excluded from payment export");

        await Add(other.Id);
        await Add(bad.Id);
        order = await Order();
        var auditCount = await setup.AuditEvents.CountAsync();
        try { await Change(order, "paid", all: true); }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or Microsoft.Data.Sqlite.SqliteException) { }
        var afterFailure = await Order();
        Assert((await Rows()).Count == 3 && afterFailure.Units.Count(x => x.PaidAt is null && x.RemovedAt is null) == 2 &&
            afterFailure.ConcurrencyToken == order.ConcurrencyToken && await setup.AuditEvents.CountAsync() == auditCount,
            "failed export rolls back earlier rows, payment, order token and audit");
        var badId = afterFailure.Units.Single(x => x.MenuItemId == bad.Id).Id;
        await Change(afterFailure, "removed", [badId]);
        order = await Order();
        Assert(await Change(order, "paid", all: true) == 200 && (await Rows()).Last().cisvyd == 43,
            "retry after failed export uses next committed document number");

        await Add(other.Id);
        other.PriceBeforeVat = 30m;
        other.Price = 36.3m;
        await setup.SaveChangesAsync();
        await Add(other.Id);
        order = await Order();
        Assert(await Change(order, "paid", all: true) == 200, "mixed historical prices paid together");
        rows = await Rows();
        var mixed = rows.Where(x => x.cisvyd == 44).ToArray();
        Assert(mixed.Length == 2 && mixed.All(x => x.cismat.Trim() == "000013" && x.mnozst == 1) &&
            mixed.Sum(x => x.procen) == 50m && mixed.Select(x => x.jedcen).SequenceEqual(new[] { 20m, 30m }),
            "same code with different historical prices preserves exact net amounts");

        // The external table is required when enabled; its absence also rolls back payment.
        await Add(other.Id);
        order = await Order();
        await setup.Database.ExecuteSqlRawAsync("DROP TABLE VYDEJ");
        try { await Change(order, "paid", all: true); throw new Exception("Missing VYDEJ accepted payment"); }
        catch (Exception error) when (error is Microsoft.Data.SqlClient.SqlException or Microsoft.Data.Sqlite.SqliteException) { }
        Assert((await Order()).Units.Any(x => x.PaidAt is null && x.RemovedAt is null), "missing VYDEJ rolls back payment");
    }

    private sealed class ExportRow
    {
        public int sklad { get; set; }
        public int cisrad { get; set; }
        public int cisvyd { get; set; }
        public DateTime datvyd { get; set; }
        public int cisobj { get; set; }
        public string drumat1 { get; set; } = "";
        public string drumat2 { get; set; } = "";
        public string cismat { get; set; } = "";
        public string nazmat { get; set; } = "";
        public int um { get; set; }
        public decimal mnozst { get; set; }
        public string ucdkl { get; set; } = "";
        public string str { get; set; } = "";
        public string zak { get; set; } = "";
        public decimal procen { get; set; }
        public string malvel { get; set; } = "";
        public decimal sazdph { get; set; }
        public decimal jedcen { get; set; }
        public string znamen { get; set; } = "";
    }
}
