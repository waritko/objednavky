# Implementation progress

## 2026-10-05 — Step 1: API persistence foundation

- Created a .NET 10 ASP.NET Core API project and configured SQLite by default, with SQL Server selectable through configuration.
- Added the initial account, catalog, table, order-unit, and audit data model. A unique active-table key is reserved for enforcing one active order per table.
- Added provider-specific initial EF Core migrations, problem-details exception handling, and process/database health endpoints.
- Verified `dotnet build` and applied the SQLite migration to a fresh local database. NuGet vulnerability lookup was unavailable in the sandbox; package restore and build succeeded from the local cache.
- SQL Server migration was generated but has not yet been applied to a running SQL Server instance.

Next: implement account bootstrap, login/logout, sessions, and role authorization. The React app and TeamCity pipeline remain later foundation work.
