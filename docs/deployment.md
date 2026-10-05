# Deployment and database setup

## Application package

Build the combined package from the repository root:

```sh
dotnet publish backend/RestaurantOrders.Api -c Release -o artifacts/api
npm --prefix frontend ci
npm --prefix frontend run build
node scripts/package.mjs
```

Deploy the entire `artifacts/api` directory to a clean release directory. It includes `wwwroot` with the React application; the API serves the UI and JSON endpoints from one origin. Node is needed for builds only. Install a current patched ASP.NET Core 10 runtime on the host. Run `dotnet RestaurantOrders.Api.dll` with the release directory as its working directory.

Use an HTTPS reverse proxy or configure Kestrel HTTPS directly. Production session/CSRF cookies require HTTPS; mobile browsers also require a secure origin for per-tap UUID generation. Bind an HTTP backend to loopback and have the proxy forward all paths without rewriting. Do not expose an HTTP production origin. Set `ASPNETCORE_ENVIRONMENT=Production`; Development is only for local HTTP tests.

## Configuration

Supply these environment variables through the service manager or secret store:

| Variable | Meaning |
| --- | --- |
| `Database__Provider` | `Sqlite` (default) or `SqlServer` |
| `ConnectionStrings__RestaurantOrders` | Database connection string; use an absolute SQLite path outside the release directory |
| `ASPNETCORE_URLS` | Backend listener, e.g. `http://127.0.0.1:5080` behind HTTPS proxy |
| `AllowedHosts` | Public hostname(s), separated by semicolons |
| `Session__Hours` | Session lifetime with sliding renewal; default 12 |
| `DataProtection__KeysPath` | Persistent directory writable only by the service account; survives redeployments |
| `Bootstrap__Username`, `Bootstrap__Password` | First-administrator credentials; remove immediately after bootstrap |

Grant the service account access only to its configuration, database and key directories. Data Protection key files must be protected by filesystem permissions and encrypted storage. Do not place database files, keys, credentials or backups under `wwwroot`.

## SQLite

Set `Database__Provider=Sqlite` and, for example, `ConnectionStrings__RestaurantOrders=Data Source=/var/lib/restaurant-orders/orders.db`. Create the containing directory first. One app instance with local durable storage is the default deployment; do not put a live SQLite database on a network share.

From the release directory, with the same configuration as the service:

```sh
dotnet RestaurantOrders.Api.dll --migrate
dotnet RestaurantOrders.Api.dll --bootstrap-admin
dotnet RestaurantOrders.Api.dll
```

Migrate before bootstrap. Bootstrap credentials must come from the environment/secret manager and are not command-line arguments. Neither migration nor bootstrap happens automatically at web startup. The `--migrate` command uses the configured provider and connection string, unlike design-time migration-generation factories.

## SQL Server

SQL Server **2022** is the tested version. Create a dedicated database and application login, set `Database__Provider=SqlServer`, and configure a connection string with TLS validation, for example `Server=sql.example;Database=RestaurantOrders;User Id=restaurant;Password=<from secret store>;Encrypt=True;TrustServerCertificate=False`. Windows integrated authentication is also supported where configured.

Run the same `--migrate` command using a deployment identity with schema permissions. The normal service identity needs application data access, not permission to create/drop databases. Provider-specific migrations live under `Persistence/Migrations/Sqlite` and `SqlServer`; never apply one provider's SQL to the other.

SQL Server's usual case-insensitive collation treats code variants as duplicates; SQLite allows case-distinct catalog codes. CSV import always matches codes ignoring case and rejects ambiguity. Use one consistent spelling for stable item/category codes.

## Operations

- Check `/health` for process availability and `/health/database` for database connectivity.
- Stop writes and take a database backup before upgrades/migrations. For SQLite, stop the service before copying the database (including any journal/WAL files), or use SQLite's online backup API. For SQL Server use database-native backups.
- Keep database and Data Protection keys outside release directories, back them up, and test restores. Historical orders are never automatically purged.
- Deploy to a new release directory to avoid stale static assets, migrate, start the service and check login/table/history flows. A rollback must account for schema compatibility; restore the backup if needed.
- Behind a reverse proxy, the built-in login limit sees the proxy address unless trusted forwarding is configured by the host. It permits ten attempts per minute per address. Do not blindly trust forwarded headers from public clients.
- There is no payment terminal integration or refund action. The application records staff declarations that payment occurred elsewhere.
