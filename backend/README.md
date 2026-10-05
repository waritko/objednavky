# Backend setup

Requires .NET SDK 10.0.201. The API uses SQLite by default. Choose SQL Server with
`Database__Provider=SqlServer` and set `ConnectionStrings__RestaurantOrders` to a
SQL Server connection string. Keep credentials in environment variables or local
user secrets.

From the repository root:

```powershell
dotnet restore backend/RestaurantOrders.Api/RestaurantOrders.Api.csproj
dotnet ef database update --project backend/RestaurantOrders.Api --context SqliteRestaurantDbContext
dotnet run --project backend/RestaurantOrders.Api
```

For SQL Server, set the two environment variables first, then run:

```powershell
dotnet ef database update --project backend/RestaurantOrders.Api --context SqlServerRestaurantDbContext
```

`GET /health` checks the API process; `GET /health/database` checks the configured
database connection. Migrations are separate per provider under `Persistence/Migrations`.

See [account setup and API contract](../docs/authentication.md) for administrator
bootstrap, sessions, account management, and the authentication smoke test.
