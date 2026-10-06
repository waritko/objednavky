# Deployment and database setup

## Application package

Build the combined ZIP package from the repository root using PowerShell
(Windows PowerShell 5.1 or PowerShell 7 on Windows/Linux):

```powershell
./scripts/build.ps1
```

The build requires the .NET 10 SDK and the Node/npm versions listed in the root
README. It installs frontend dependencies, builds React, publishes the API in
Release mode, and creates `artifacts/restaurant-orders-app.zip`. Each build uses
a fresh `artifacts/publish-<id>` directory, retained for inspection; old publish
directories can be removed when no longer needed. A successful build replaces
the ZIP. Existing `artifacts/api` deployments and their data are left intact.

By default, the script then copies the contents of the publish directory (rather
than the ZIP or its enclosing directory) via SCP to
`waritko@mrazitko.varak.net:/home/waritko/objednavky-run`. The remote directory must
already exist and be writable. OpenSSH `scp` must be on PATH; authentication and
host verification use your SSH configuration. The copy overwrites matching files,
retains other remote files, and does not restart the application. A failed copy
fails the script while retaining the local publish directory and ZIP.

Configure the destination, SSH port, or private key, or skip copying:

```powershell
./scripts/build.ps1 -ScpDestination 'user@host:/srv/restaurant' -ScpPort 2222 -ScpIdentityFile "$env:USERPROFILE/.ssh/id_ed25519"
./scripts/build.ps1 -SkipScp
```

Extract the ZIP into a clean release directory. It includes `wwwroot` with the
React application; the API serves the UI and JSON endpoints from one origin.
This framework-dependent package works on Windows and Linux. Install a current
patched ASP.NET Core 10 runtime on the host; Node and PowerShell are only needed
for building.

Run `run.cmd` on Windows or `bash run.sh` on Linux. Both launchers select the
release directory as their working directory, preserve environment configuration,
and forward arguments. For first-time setup, configure the database and bootstrap
credentials as described below, then run. The administrator creation step is
optional: without it, first startup on an empty accounts table creates `jana`
(administrator, password `Lucie`) and `monami` (standard, password `Kava`).

| Action | Windows | Linux |
| --- | --- | --- |
| Apply migrations | `.\run.cmd --migrate` | `bash run.sh --migrate` |
| Create administrator | `.\run.cmd --bootstrap-admin` | `bash run.sh --bootstrap-admin` |
| Start application | `.\run.cmd` | `bash run.sh` |

Remove bootstrap credentials from the environment after creating the administrator.
On Linux you may also run `chmod +x run.sh` and then `./run.sh`.

Use an HTTPS reverse proxy or configure Kestrel HTTPS directly. Production session/CSRF cookies require HTTPS; mobile browsers also require a secure origin for per-tap UUID generation. Bind an HTTP backend to loopback and have the proxy forward all paths without rewriting. Do not expose an HTTP production origin. Set `ASPNETCORE_ENVIRONMENT=Production`; Development is only for local HTTP tests.

The API processes `X-Forwarded-Proto` and `X-Forwarded-For` before authentication
and CSRF validation. Configure the proxy to overwrite these headers with the
original request scheme and client address. Loopback proxies are trusted by default.
For a proxy on another host, set `ReverseProxy__KnownProxies__0` to its backend-facing
IP address (add `__1`, `__2`, etc. for additional trusted addresses). Only one proxy
hop is processed; do not accept forwarded headers directly from public clients.
For example, an Nginx HTTPS server forwarding to the loopback backend should use:

```nginx
location / {
    proxy_pass http://127.0.0.1:5080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Forwarded-For $remote_addr;
}
```

If `/auth/csrf` reports that `Cookie.SecurePolicy = Always` requires SSL, check
that the public URL uses HTTPS and that the trusted proxy sends
`X-Forwarded-Proto: https`. For local HTTP testing, use
`ASPNETCORE_ENVIRONMENT=Development` before starting the application.

## Configuration

Supply these environment variables through the service manager or secret store:

| Variable | Meaning |
| --- | --- |
| `Database__Provider` | `Sqlite` (default) or `SqlServer` |
| `ConnectionStrings__RestaurantOrders` | Database connection string; use an absolute SQLite path outside the release directory |
| `ASPNETCORE_URLS` | Backend listener, e.g. `http://127.0.0.1:5080` behind HTTPS proxy |
| `AllowedHosts` | Public hostname(s), separated by semicolons |
| `ReverseProxy__KnownProxies__0` | Additional trusted proxy IP; loopback proxies are trusted by default |
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

Startup creates a missing database and applies pending migrations before initializing
default accounts. Both `--bootstrap-admin` and `--migrate` also apply migrations;
`--migrate` exits without creating accounts. The explicit migration step above is
optional. Bootstrap credentials must come from the environment/secret manager and
are not command-line arguments. Custom administrator bootstrap remains an explicit
command. Migrations use the configured provider and connection string, unlike
design-time migration-generation factories.

## SQL Server

SQL Server **2022** is the tested version. Create a dedicated database and application login, set `Database__Provider=SqlServer`, and configure a connection string with TLS validation, for example `Server=sql.example;Database=RestaurantOrders;User Id=restaurant;Password=<from secret store>;Encrypt=True;TrustServerCertificate=False`. Windows integrated authentication is also supported where configured.

Startup applies pending migrations, so the service identity needs schema permissions
when migrations are pending and database creation permission if the database does
not exist. Alternatively, provision the database and run `--migrate` using a deployment
identity before starting with an application data access identity. Provider-specific
migrations live under `Persistence/Migrations/Sqlite` and `SqlServer`; never apply
one provider's SQL to the other.

SQL Server's usual case-insensitive collation treats code variants as duplicates; SQLite allows case-distinct catalog codes. CSV import always matches codes ignoring case and rejects ambiguity. Use one consistent spelling for stable item/category codes.

## Operations

- Check `/health` for process availability and `/health/database` for database connectivity.
- Stop writes and take a database backup before upgrades/migrations. For SQLite, stop the service before copying the database (including any journal/WAL files), or use SQLite's online backup API. For SQL Server use database-native backups.
- Keep database and Data Protection keys outside release directories, back them up, and test restores. Historical orders are never automatically purged.
- Deploy to a new release directory to avoid stale static assets, migrate, start the service and check login/table/history flows. A rollback must account for schema compatibility; restore the backup if needed.
- The built-in login limit permits ten attempts per minute per client address. Behind a trusted proxy, it uses `X-Forwarded-For`; otherwise it sees the proxy address.
- There is no payment terminal integration or refund action. The application records staff declarations that payment occurred elsewhere.
