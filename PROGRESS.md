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

## 2026-10-05 — Step 3: Table and catalog administration API

- Added authenticated table, category, subcategory, and menu-item lists, sorted by configured order, name, and ID. Creation and full updates require the Administrator role and the existing CSRF protection.
- Added trimmed names/codes, length and duplicate-code validation, category/subcategory relationship checks, and server-calculated VAT-inclusive prices using decimal arithmetic and midpoint-away-from-zero rounding. Enforced common decimal scale and bounds for both database providers.
- Added atomic bulk subcategory assignment and clearing. Prevented moving an assigned subcategory to another category. Catalog writes use serializable transactions.
- Added enabled-state editing rather than destructive deletion, preserving records and historical references. Lists include disabled records; future ordering logic must enforce enabled state for items and their parents.
- Documented request/response contracts, defaults, error codes, sorting, price rules, and disable behavior in `docs/catalog.md`.
- Verified `dotnet run --project backend/RestaurantOrders.SmokeTests`: API/test projects built and authentication plus catalog HTTP checks passed against a freshly migrated temporary SQLite database. Checks cover anonymous/operational access, sorting, trimming and leading zeroes, invalid prices, VAT rounding, duplicate codes, missing parents, category mismatches, assignment atomicity, edits, and disabled-record retention. `git diff --check` passed.
- Existing NuGet warnings NU1900 (vulnerability feed unavailable) and NU1510 (explicit Identity dependency) remain. No schema change was required. SQL Server execution remains unverified without an isolated instance.

Next: backend step 4, CSV preview and transactional import using `ciselnik.csv`. React administration, the operational frontend, TeamCity, and SQL Server verification remain pending; this step completes the catalog administration API, not the full stage-2 acceptance gate.

## 2026-10-05 — Step 4: CSV preview and transactional import

- Added administrator-only CSV template download, preview, and commit endpoints. Preview accepts decoded CSV text, returns validated rows and Czech row-level errors, and makes no catalog changes. A protected, account-bound token confirms those rows within 30 minutes.
- Added quoted-field parsing, whitespace trimming, preserved alphanumeric/leading-zero codes, empty-row handling, duplicate rejection, shared price validation, and VAT-inclusive decimal rounding.
- Added transactional upserts with stable item IDs, name/price/VAT/category updates, category creation, and subcategory clearing only when the category changes. Existing enabled states, category display names, and sorting remain intact; order snapshots are untouched.
- Found that the sample's 11 categories use 13 case-sensitive spellings (`k`/`K` and `z`/`Z`). Import matches codes ignoring case, retains existing spelling, and rejects ambiguous existing variants. Documented this and the retry/update behavior in `docs/csv-import.md`.
- Verified `dotnet run --no-restore --project backend/RestaurantOrders.SmokeTests`: authentication, catalog, and CSV checks passed against fresh SQLite. Import checks cover all 201 sample items and nine empty records, 11 category creation, quoting, row errors, rounding, authorization, template download, preview isolation, tampered tokens, repeated import, subcategory retention/clearing, and full rollback after a late conflict. `git diff --check` passed.
- No migration was needed. Existing NU1900 vulnerability-feed and NU1510 dependency warnings remain. SQL Server execution is still pending an isolated instance; frontend import UI is not yet implemented.

Next: backend step 5, atomic table/order creation and additions, unit removal, per-unit and bulk Processed/Paid actions. React, TeamCity, and SQL Server verification remain pending delivery work.
