using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using RestaurantOrders.Api.Domain;

internal static class MigrationChecks
{
    public static async Task Run(bool sqlServer)
    {
        foreach (var withNotes in new[] { false, true })
        {
            await using var database = new TestDatabase(sqlServer);
            await database.Initialize(migrate: false);
            await using var db = database.Context();
            var migrations = db.Database.GetMigrations().ToArray();
            await db.GetService<IMigrator>().MigrateAsync(migrations[withNotes ? 1 : 0]);
            var actor = Guid.NewGuid();
            var table = Guid.NewGuid();
            var category = Guid.NewGuid();
            var subcategory = Guid.NewGuid();
            var item = Guid.NewGuid();
            var order = Guid.NewGuid();
            var unit = Guid.NewGuid();
            var audit = Guid.NewGuid();
            var token = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            // Distractors ensure keys from different tables map independently.
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Accounts (Id, Username, PasswordHash, Role, Enabled) VALUES ({Guid.Empty}, {"other"}, {"unchanged"}, {"Operational"}, {false})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Tables (Id, Name, SortOrder, Enabled) VALUES ({Guid.Empty}, {"Other table"}, {10}, {false})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Categories (Id, Code, Name, SortOrder, Enabled) VALUES ({Guid.Empty}, {"OTHER"}, {"Other category"}, {10}, {false})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Accounts (Id, Username, PasswordHash, Role, Enabled) VALUES ({actor}, {"legacy"}, {"unused"}, {"Administrator"}, {true})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Tables (Id, Name, SortOrder, Enabled) VALUES ({table}, {"Legacy table"}, {0}, {true})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Categories (Id, Code, Name, SortOrder, Enabled) VALUES ({category}, {"LEGACY"}, {"Legacy category"}, {0}, {true})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Subcategories (Id, CategoryId, Name, SortOrder, Enabled) VALUES ({subcategory}, {category}, {"Legacy subcategory"}, {0}, {true})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO MenuItems (Id, CategoryId, SubcategoryId, Code, Name, PriceBeforeVat, VatRate, Price, SortOrder, Enabled) VALUES ({item}, {category}, {subcategory}, {"LEGACY"}, {"Legacy item"}, {10m}, {12m}, {11.2m}, {0}, {true})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Orders (Id, TableId, ActiveTableId, CreatedByAccountId, OpenedAt, State, ConcurrencyToken) VALUES ({order}, {table}, {table}, {actor}, {now}, {"Active"}, {token})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Orders (Id, TableId, CreatedByAccountId, OpenedAt, ClosedAt, State, ConcurrencyToken) VALUES ({Guid.Empty}, {Guid.Empty}, {Guid.Empty}, {now}, {now}, {"Closed"}, {Guid.Empty})");
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO OrderUnits (Id, OrderId, MenuItemId, ItemName, CategoryName, SubcategoryName, UnitPrice, AddedAt, AddedByAccountId, ProcessedAt, ProcessedByAccountId, PaidAt, PaidByAccountId, RemovedAt, RemovedByAccountId) VALUES ({unit}, {order}, {item}, {"Historical item"}, {"Historical category"}, {"Historical subcategory"}, {11.2m}, {now}, {actor}, {now}, {actor}, {now}, {actor}, {now}, {actor})");
            const string note = "Bez soli \"prosím\"\nČesky";
            var details = JsonSerializer.Serialize(new { MenuItemId = item, before = (string?)null, after = note });
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AuditEvents (Id, OrderId, ActorAccountId, OccurredAt, Action, UnitId, DetailsJson) VALUES ({audit}, {order}, {actor}, {now}, {"NoteChanged"}, {unit}, {details})");
            if (withNotes)
            {
                var notes = JsonSerializer.Serialize(new Dictionary<Guid, string> { [item] = note });
                await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Orders SET Note = {note}, LineNotesJson = {notes} WHERE Id = {order}");
            }
            await db.Database.MigrateAsync();
            var preserved = await db.Orders.Include(x => x.Units).SingleAsync(x => x.State == OrderState.Active);
            var closed = await db.Orders.SingleAsync(x => x.State == OrderState.Closed);
            var preservedItem = await db.MenuItems.SingleAsync();
            var preservedUnit = preserved.Units.Single();
            var preservedAudit = await db.AuditEvents.SingleAsync();
            if (preserved.Id <= 0 || preserved.TableId != (await db.Tables.SingleAsync(x => x.Name == "Legacy table")).Id ||
                preserved.ActiveTableId != preserved.TableId || preserved.CreatedByAccountId != (await db.Accounts.SingleAsync(x => x.Username == "legacy")).Id ||
                preservedItem.CategoryId != (await db.Categories.SingleAsync(x => x.Code == "LEGACY")).Id ||
                preservedItem.SubcategoryId != (await db.Subcategories.SingleAsync()).Id ||
                closed.ActiveTableId is not null || closed.ClosedAt is null || closed.ConcurrencyToken != Guid.Empty ||
                closed.TableId != (await db.Tables.SingleAsync(x => x.Name == "Other table")).Id ||
                (await db.Accounts.SingleAsync(x => x.Username == "other")).PasswordHash != "unchanged" ||
                preserved.ConcurrencyToken != token || preservedUnit.RequestToken != unit ||
                preservedUnit.MenuItemId != preservedItem.Id || preservedUnit.UnitPrice != 11.2m ||
                preservedUnit.ItemCode != "LEGACY" || preservedUnit.CategoryCode != "LEGACY" ||
                preservedUnit.UnitPriceBeforeVat != 10m || preservedUnit.VatRate != 12m ||
                preservedUnit.ItemName != "Historical item" || preservedUnit.OrderId != preserved.Id ||
                preservedUnit.AddedByAccountId != preserved.CreatedByAccountId ||
                preservedUnit.ProcessedByAccountId != preserved.CreatedByAccountId ||
                preservedUnit.PaidByAccountId != preserved.CreatedByAccountId ||
                preservedUnit.RemovedByAccountId != preserved.CreatedByAccountId ||
                preservedAudit.OrderId != preserved.Id || preservedAudit.UnitId != preservedUnit.Id ||
                preservedAudit.ActorAccountId != preserved.CreatedByAccountId ||
                JsonDocument.Parse(preservedAudit.DetailsJson!).RootElement.GetProperty("MenuItemId").GetInt32() != preservedItem.Id)
                throw new Exception("Integer ID migration lost data or references.");
            var lineNotes = JsonSerializer.Deserialize<Dictionary<int, string>>(preserved.LineNotesJson)!;
            if (withNotes ? preserved.Note != note || lineNotes[preservedItem.Id] != note : preserved.Note is not null || lineNotes.Count != 0)
                throw new Exception("Integer ID migration did not preserve order and line notes.");
            // Identity/autoincrement must continue beyond copied rows on both providers.
            var next = new RestaurantTable { Name = "After migration" };
            db.Tables.Add(next);
            await db.SaveChangesAsync();
            if (next.Id <= preserved.TableId || db.Database.HasPendingModelChanges())
                throw new Exception("Generated integer IDs or migration snapshot are invalid.");
            if (!sqlServer)
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                await db.Database.OpenConnectionAsync();
                command.CommandText = "PRAGMA foreign_key_check";
                await using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync()) throw new Exception("Migration left broken SQLite foreign keys.");
            }
            Console.WriteLine($"PASS populated GUID database upgrade {(withNotes ? "with" : "before")} notes, integer keys, references, audit and generated IDs ({database.Provider})");
        }
    }
}
