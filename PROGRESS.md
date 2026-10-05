# Implementation progress

## 2026-10-05 — Step 1: API persistence foundation

- Created a .NET 10 ASP.NET Core API project and configured SQLite by default, with SQL Server selectable through configuration.
- Added the initial account, catalog, table, order-unit, and audit data model. A unique active-table key is reserved for enforcing one active order per table.
- Added provider-specific initial EF Core migrations, problem-details exception handling, and process/database health endpoints.
- Verified `dotnet build` and applied the SQLite migration to a fresh local database. NuGet vulnerability lookup was unavailable in the sandbox; package restore and build succeeded from the local cache.
- SQL Server migration was generated but has not yet been applied to a running SQL Server instance.

Next: implement account bootstrap, login/logout, sessions, and role authorization. The React app and TeamCity pipeline remain later foundation work.

## 2026-10-05 — Step 2: Accounts and authentication

- Completed the existing unfinished authentication wiring with cookie login/logout, current-account lookup, password hashing, configurable session lifetime, and per-request account validation.
- Added a command-line first-administrator bootstrap that requires an empty account table and reads credentials from configuration. Added administrator-only account listing, creation, password reset, role changes, and disabling, with normalized unique usernames and protection for the last enabled administrator.
- Added CSRF validation for all unsafe HTTP methods, including login and PUT; login rate limiting; secure production cookies; and session invalidation after account password, role, or username changes.
- Documented bootstrap, deployment/session requirements, endpoint contracts, Czech error codes, and verification in `docs/authentication.md`.
- Verified API build and the executable HTTP smoke suite against a freshly migrated SQLite database: bootstrap and repeat rejection, both roles, anonymous/operational administration rejection, missing CSRF, bad password, duplicate username, last-administrator protection, password reset, account disabling, session invalidation, and logout all passed.
- The smoke suite required execution outside the sandbox because ASP.NET Data Protection writes to the user's key directory. NuGet vulnerability lookup still reported NU1900 after an elevated restore; restore/build completed. NuGet also reports NU1510 for the pre-existing explicit Identity package reference.
- SQL Server migration/authentication verification remains pending; no SQL Server test instance was used in this step.

Next: table and catalog CRUD with sorting, enabled state, and price validation (backend step 3), then CSV preview/import. React and TeamCity remain pending foundation deliverables; the full stage-1 acceptance gate is not yet complete.
