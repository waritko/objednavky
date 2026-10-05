# Objednávky restaurace

Czech mobile application for waiters, kitchen staff and administrators. ASP.NET Core 10 / React / TypeScript, with SQLite or SQL Server persistence.

One active order per table; one saved unit per tap; independent delivery/payment per unit; retained corrections and audit history; automatic closure only after every current unit is delivered and paid.

## Start locally

Install .NET SDK **10.0.201**, Node **25.9.0** and npm **11.12.1**. From the repository root, using PowerShell:

```powershell
dotnet restore backend/RestaurantOrders.SmokeTests
dotnet run --project backend/RestaurantOrders.Api -- --migrate
$env:Bootstrap__Username = 'admin'
$bootstrapPassword = Read-Host 'Initial administrator password (12+ characters)' -AsSecureString
$env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', $bootstrapPassword).Password
dotnet run --project backend/RestaurantOrders.Api -- --bootstrap-admin
Remove-Item Env:Bootstrap__Password,Env:Bootstrap__Username
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --no-launch-profile --project backend/RestaurantOrders.Api --urls http://127.0.0.1:5080
```

In a second terminal:

```powershell
npm --prefix frontend ci
npm --prefix frontend run dev
```

Open the Vite URL and sign in. In **Správa**, configure tables/menu and create operational accounts. The bootstrap command refuses to overwrite existing accounts.

## Guides

- [Staff guide (Czech)](docs/staff-guide.cs.md)
- [Production setup, providers and backups](docs/deployment.md)
- [Accounts and sessions](docs/authentication.md)
- [Catalog API](docs/catalog.md), [CSV guide](docs/csv-import.md), [orders API](docs/orders.md)
- [Testing and exact verified versions](docs/testing.md)
- [TeamCity/Linux agent setup](docs/teamcity.md)
- [Implementation plan](PLAN.md) and [progress record](PROGRESS.md)

`bash scripts/ci.sh` runs formatting/type checks, builds, SQLite and isolated SQL Server tests, frontend tests, packages the application, and runs both phone workflows against the actual package. It requires Docker and Playwright browser dependencies. Output: `artifacts/api`, archived by TeamCity as `restaurant-orders-app.zip`.
