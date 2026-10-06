# Verification

From the repository root:

```sh
dotnet run --project backend/RestaurantOrders.SmokeTests
node scripts/sqlserver-tests.mjs
npm --prefix frontend test
npm --prefix frontend run test:e2e
```

The executable backend suite verifies actual API startup with missing and empty
databases, automatic migrations/default accounts, preservation of changed credentials
and data on restart, and administrator bootstrap without prior migration.
It also runs real HTTP requests covering authentication, authorization, catalog
validation, CSV parsing/import, saved order snapshots, partial delivery/payment,
corrections, closure, history, audit and concurrent devices. It fails on a nonzero
exit code; `dotnet test` does not run this executable.

SQL Server tests require Docker with Linux containers and at least 2 GB available memory. The runner starts a disposable SQL Server 2022 Developer container with a random password and loopback-only ephemeral port, runs the same suite, then removes the container even on failure. The image is pinned by digest in `scripts/sqlserver-image.txt`. No database or credentials from application configuration are used. With Release builds, set `BUILD_CONFIGURATION=Release`.

Alternatively, set `TEST_SQLSERVER_CONNECTION` to a dedicated test instance and run `dotnet run --project backend/RestaurantOrders.SmokeTests -- --sqlserver`. The login must be able to create/drop databases. The harness overrides the database name with a generated `RestaurantOrders_Test_<UUID>`, creates it, migrates only that database, and deletes only that database. Never point this setting at a production instance.

SQLite permits case-distinct catalog codes; SQL Server's default case-insensitive collation prevents them. The suite verifies ambiguous-import rollback on SQLite and collision rejection on SQL Server. Both providers run the complete order/concurrency scenarios. EF may log expected deadlock errors during concurrency checks; the HTTP assertions require 409 with recoverable retries, never 500.

Browser tests need `npm --prefix frontend ci` and `npx playwright install chromium` from `frontend/`, plus a built API. They own local ports 5080 and 5173, create a temporary database, bootstrap test-only accounts and shut down their servers. Tests use 390×844 and 360×800 touch viewports; traces/screenshots remain under `frontend/test-results` on failure and successful phone screenshots/JUnit results under `artifacts/`.

Verified versions: .NET SDK 10.0.201; local ASP.NET/.NET runtime 10.0.12; EF Core 10.0.5; SQLite 3.53.3; SQL Server 2022 16.0.4295.3 (Linux container); Node 25.9.0; npm 11.12.1; Chromium 153.0.8010.12 / Playwright 1.63.0. Frontend package versions are exact in the lockfile.

The native SQLite dependency is explicitly pinned to 3.53.3 because the previous
2.1.11 package bundled vulnerable SQLite 3.49.1. See the
[SQLite dependency advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).
Restore and Release builds now complete without NuGet warnings.

The backend restore, C# formatting check and full SQLite HTTP/concurrency suite
also passed inside the native Linux `.NET SDK 10.0.201` container, using a read-only
source mount and a separate temporary build directory.

The complete `bash scripts/ci.sh` sequence also runs the browser tests against the
published API with its production-built frontend, not only Vite. These include a
server-committed tap whose response is lost, safe retry, cancelled paid removal,
confirmed paid removal, and read-only history. Test servers use Development cookies
on loopback HTTP; production HTTPS/reverse-proxy configuration is deployment-specific.
