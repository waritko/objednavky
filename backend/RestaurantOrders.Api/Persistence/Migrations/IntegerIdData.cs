using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantOrders.Api.Persistence.Migrations;

// Fixed schema for the IntegerIds migrations; do not depend on the live EF model.
internal static class IntegerIdData
{
    private static readonly string[] Tables = ["Accounts", "Categories", "Tables", "Subcategories", "MenuItems", "Orders", "OrderUnits", "AuditEvents"];
    private static bool SqlServer(MigrationBuilder migration) => migration.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer";
    private static string StageName(string table, bool sqlServer) => sqlServer ? $"[#IntegerIds_{table}]" : $"\"IntegerIds_{table}\"";
    private static string MapName(string table, bool sqlServer) => sqlServer ? $"[#IntegerIds_Map_{table}]" : $"\"IntegerIds_Map_{table}\"";

    public static void Stage(MigrationBuilder migration)
    {
        var sqlServer = SqlServer(migration);
        foreach (var table in Tables)
        {
            var stage = StageName(table, sqlServer);
            var map = MapName(table, sqlServer);
            migration.Sql(sqlServer
                ? $"SELECT * INTO {stage} FROM [{table}] WITH (TABLOCKX, HOLDLOCK); SELECT [Id] AS [OldId], CAST(ROW_NUMBER() OVER (ORDER BY [Id]) AS int) AS [NewId] INTO {map} FROM {stage};"
                : $"CREATE TEMP TABLE {stage} AS SELECT * FROM [{table}]; CREATE TEMP TABLE {map} AS SELECT [Id] AS [OldId], ROW_NUMBER() OVER (ORDER BY [Id]) AS [NewId] FROM {stage};");
            migration.Sql($"CREATE UNIQUE INDEX [IX_IntegerIds_Map_{table}] ON {map} ([OldId]);");
        }
    }

    public static void Copy(MigrationBuilder migration)
    {
        var sqlServer = SqlServer(migration);
        string Map(string table, string column) => $"(SELECT [NewId] FROM {MapName(table, sqlServer)} WHERE [OldId] = s.[{column}])";
        void CopyTable(string table, string columns, params string[] values)
        {
            var insert = $"INSERT INTO [{table}] ({columns}) SELECT {string.Join(", ", values)} FROM {StageName(table, sqlServer)} s;";
            migration.Sql(sqlServer ? $"SET IDENTITY_INSERT [{table}] ON; {insert} SET IDENTITY_INSERT [{table}] OFF;" : insert);
        }
        CopyTable("Accounts", "[Id], [Username], [PasswordHash], [Role], [Enabled]", Map("Accounts", "Id"), "s.[Username]", "s.[PasswordHash]", "s.[Role]", "s.[Enabled]");
        CopyTable("Categories", "[Id], [Code], [Name], [SortOrder], [Enabled]", Map("Categories", "Id"), "s.[Code]", "s.[Name]", "s.[SortOrder]", "s.[Enabled]");
        CopyTable("Tables", "[Id], [Name], [SortOrder], [Enabled]", Map("Tables", "Id"), "s.[Name]", "s.[SortOrder]", "s.[Enabled]");
        CopyTable("Subcategories", "[Id], [CategoryId], [Name], [SortOrder], [Enabled]", Map("Subcategories", "Id"), Map("Categories", "CategoryId"), "s.[Name]", "s.[SortOrder]", "s.[Enabled]");
        CopyTable("MenuItems", "[Id], [Code], [CategoryId], [SubcategoryId], [Name], [PriceBeforeVat], [VatRate], [Price], [SortOrder], [Enabled]", Map("MenuItems", "Id"), "s.[Code]", Map("Categories", "CategoryId"), Map("Subcategories", "SubcategoryId"), "s.[Name]", "s.[PriceBeforeVat]", "s.[VatRate]", "s.[Price]", "s.[SortOrder]", "s.[Enabled]");
        var notes = sqlServer
            ? $"COALESCE((SELECT N'{{' + STRING_AGG(CAST(N'\"' + CAST(m.[NewId] AS nvarchar(20)) + N'\":\"' + STRING_ESCAPE(j.[value], 'json') + N'\"' AS nvarchar(max)), N',') + N'}}' FROM OPENJSON(s.[LineNotesJson]) j JOIN {MapName("MenuItems", true)} m ON m.[OldId] = TRY_CONVERT(uniqueidentifier, j.[key])), N'{{}}')"
            : $"(SELECT json_group_object(CAST(m.[NewId] AS TEXT), j.[value]) FROM json_each(s.[LineNotesJson]) j JOIN {MapName("MenuItems", false)} m ON m.[OldId] = j.[key] COLLATE NOCASE)";
        CopyTable("Orders", "[Id], [TableId], [ActiveTableId], [CreatedByAccountId], [OpenedAt], [ClosedAt], [State], [ConcurrencyToken], [Note], [LineNotesJson]", Map("Orders", "Id"), Map("Tables", "TableId"), Map("Tables", "ActiveTableId"), Map("Accounts", "CreatedByAccountId"), "s.[OpenedAt]", "s.[ClosedAt]", "s.[State]", "s.[ConcurrencyToken]", "s.[Note]", notes);
        CopyTable("OrderUnits", "[Id], [OrderId], [RequestToken], [MenuItemId], [ItemName], [CategoryName], [SubcategoryName], [UnitPrice], [AddedAt], [AddedByAccountId], [ProcessedAt], [ProcessedByAccountId], [PaidAt], [PaidByAccountId], [RemovedAt], [RemovedByAccountId]", Map("OrderUnits", "Id"), Map("Orders", "OrderId"), "s.[Id]", Map("MenuItems", "MenuItemId"), "s.[ItemName]", "s.[CategoryName]", "s.[SubcategoryName]", "s.[UnitPrice]", "s.[AddedAt]", Map("Accounts", "AddedByAccountId"), "s.[ProcessedAt]", Map("Accounts", "ProcessedByAccountId"), "s.[PaidAt]", Map("Accounts", "PaidByAccountId"), "s.[RemovedAt]", Map("Accounts", "RemovedByAccountId"));
        // NoteChanged is the only historical details payload containing an entity ID.
        var details = sqlServer
            ? $"CASE WHEN s.[Action] = N'NoteChanged' AND JSON_VALUE(s.[DetailsJson], '$.MenuItemId') IS NOT NULL THEN JSON_MODIFY(s.[DetailsJson], '$.MenuItemId', (SELECT [NewId] FROM {MapName("MenuItems", true)} WHERE [OldId] = TRY_CONVERT(uniqueidentifier, JSON_VALUE(s.[DetailsJson], '$.MenuItemId')))) ELSE s.[DetailsJson] END"
            : $"CASE WHEN s.[Action] = 'NoteChanged' AND json_extract(s.[DetailsJson], '$.MenuItemId') IS NOT NULL THEN json_set(s.[DetailsJson], '$.MenuItemId', (SELECT [NewId] FROM {MapName("MenuItems", false)} WHERE [OldId] = json_extract(s.[DetailsJson], '$.MenuItemId') COLLATE NOCASE)) ELSE s.[DetailsJson] END";
        CopyTable("AuditEvents", "[Id], [OrderId], [ActorAccountId], [OccurredAt], [Action], [UnitId], [DetailsJson]", Map("AuditEvents", "Id"), Map("Orders", "OrderId"), Map("Accounts", "ActorAccountId"), "s.[OccurredAt]", "s.[Action]", Map("OrderUnits", "UnitId"), details);
        foreach (var table in Tables)
            migration.Sql($"DROP TABLE {StageName(table, sqlServer)}; DROP TABLE {MapName(table, sqlServer)};");
    }
}
