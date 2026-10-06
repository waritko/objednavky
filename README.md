# Objednávky restaurace

Czech mobile application for waiters, kitchen staff and administrators. ASP.NET Core 10 / React / TypeScript, with SQLite or SQL Server persistence.

One active order per table; one saved unit per tap; independent delivery/payment per unit; retained corrections and audit history; automatic closure only after every current unit is delivered and paid.

## Start locally

Install any stable .NET **10.0** SDK, Node **25.9.0** and npm **11.12.1**. From the repository root, using PowerShell:

```powershell
.\scripts\start-local.ps1
```

On subsequent runs, use `.\scripts\start-local.ps1` (add `-SkipInstall` to reuse
installed frontend dependencies). The script builds the API, applies migrations,
and starts both servers. Open **http://127.0.0.1:5173**; press **Ctrl+C** to stop.
When the accounts table is empty, startup creates administrator `jana` (password
`Lucie`) and standard account `monami` (password `Kava`). Existing accounts are
left unchanged. Use `-BootstrapAdmin` on a fresh database to supply a custom
administrator instead. Database provider and connection-string environment settings
are respected, so check those before running migrations against a custom database.

To run the same steps manually:

```powershell
dotnet restore backend/RestaurantOrders.SmokeTests
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-launch-profile --project backend/RestaurantOrders.Api --urls http://127.0.0.1:5080
```

In a second terminal:

```powershell
npm --prefix frontend ci
npm --prefix frontend run dev
```

The API creates a missing database and applies pending migrations at startup.
Open the Vite URL and sign in. In **Správa**, configure tables/menu and create operational accounts. The bootstrap command refuses to overwrite existing accounts.

## Build a deployment ZIP

Run `./scripts/build.ps1` in PowerShell to build the frontend, publish the API,
and create `artifacts/restaurant-orders-app.zip`. On Linux, use PowerShell 7:
`pwsh -File scripts/build.ps1`.

Extract the ZIP and use `run.cmd` on Windows or `bash run.sh` on Linux.
The host needs the ASP.NET Core 10 runtime. See the
[deployment guide](docs/deployment.md) for database initialization and HTTPS setup.

## Guides

- [Staff guide (Czech)](docs/staff-guide.cs.md)
- [Production setup, providers and backups](docs/deployment.md)
- [Accounts and sessions](docs/authentication.md)
- [Catalog API](docs/catalog.md), [CSV guide](docs/csv-import.md), [orders API](docs/orders.md)
- [Testing and exact verified versions](docs/testing.md)
- [TeamCity/Linux agent setup](docs/teamcity.md)
- [Implementation plan](PLAN.md) and [progress record](PROGRESS.md)

`bash scripts/ci.sh` runs formatting/type checks, builds, SQLite and isolated SQL Server tests, frontend tests, packages the application, and runs both phone workflows against the actual package. It requires Docker and Playwright browser dependencies. Output: `artifacts/api`, archived by TeamCity as `restaurant-orders-app.zip`.
