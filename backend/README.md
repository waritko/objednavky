# Backend setup

Requires any stable .NET 10.0 SDK. The API uses SQL Server by default at
`mrazitko.varak.net`, database `RestaurantOrders`, login `monami`, with TLS certificate
validation. Supply the password using `Database__Password`; no password is stored
in the repository. Override `ConnectionStrings__RestaurantOrders` for another
connection. Keep credentials in environment variables or local user secrets.
For local SQLite, explicitly set `Database__Provider=Sqlite` and
`ConnectionStrings__RestaurantOrders=Data Source=restaurant-orders.db`.

From the repository root:

```powershell
dotnet restore backend/RestaurantOrders.Api/RestaurantOrders.Api.csproj
dotnet run --project backend/RestaurantOrders.Api
```

Startup creates a missing database, applies pending provider-specific migrations,
and initializes default accounts when the accounts table is empty. Existing data
and accounts are preserved. This also applies before `--bootstrap-admin`.

For the default SQL Server, set `Database__Password` first. To apply migrations and
exit without starting the server or creating default accounts, run:

```powershell
dotnet run --project backend/RestaurantOrders.Api -- --migrate
```

`GET /health` checks the API process; `GET /health/database` checks the configured
database connection. Migrations are separate per provider under `Persistence/Migrations`.

See [account setup and API contract](../docs/authentication.md) for administrator
bootstrap, sessions, account management, and the authentication smoke test.

See [catalog API](../docs/catalog.md) and [CSV import](../docs/csv-import.md) for
menu administration and the preview/commit import workflow.

See [TeamCity CI setup](../docs/teamcity.md) for the Linux build agent requirements,
versioned settings import, and local CI script.

See the [root quick start](../README.md), [deployment guide](../docs/deployment.md),
[orders API](../docs/orders.md) and [provider/browser verification](../docs/testing.md).
The explicit `--migrate` command respects runtime configuration. Design-time factories
are for generating migrations; do not use their hard-coded development connections
to apply production migrations.
