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

## 2026-10-05 — TeamCity CI configuration

- Added `.teamcity/settings.kts` targeting TeamCity 2026.1 (build 222521), with Linux agents as requested. The build uses the settings repository VCS root, a default-branch trigger, clean checkout, and a 20-minute timeout.
- Added `scripts/ci.sh` with restore, Release build, executable SQLite HTTP smoke tests, and API publish steps. Failures stop the pipeline; the smoke suite reports a TeamCity test result and the API is archived as `restaurant-orders-api.zip`.
- Pinned .NET SDK 10.0.201 in `global.json`, enforced LF for shell scripts, ignored build artifacts, and fixed the smoke harness to locate the API in the actual build configuration instead of hard-coded Debug.
- Documented Linux agent prerequisites, versioned-settings import, repository credentials, artifact behavior, local reproduction, and remaining coverage in `docs/teamcity.md`.
- Verified Bash syntax and the entire CI script using Git Bash on this Windows workstation: restore, Release build, all authentication/catalog/import checks against fresh SQLite, and API publish passed. Existing NU1900 vulnerability-feed and NU1510 dependency warnings remain. `git diff --check` passed.
- TeamCity DSL import and a native Linux agent run remain unverified: this session has no connected TeamCity server or Linux agent. SQL Server, frontend, formatting, and phone-flow checks are not yet implemented in CI; this is the current backend pipeline, not the full planned acceptance gate.

Next: resume backend step 5 (ordering), then expand CI as the remaining application and provider tests are implemented.

## 2026-10-05 — Step 5: Atomic orders and unit actions

- Implemented atomic table-order creation and additions, immutable item/category/price snapshots, and enabled-parent validation. Client-generated unit IDs make retries safe without suppressing intentional repeated taps.
- Added selected-unit and whole-order delivery/payment/removal, guarded paid removal, authoritative totals, optimistic concurrency, serializable transactions, and automatic closure/table reuse. Removed units retain payment history and actor/time records.
- Added transactional audit events alongside mutations and documented the API in `docs/orders.md`.
- Verified the complete SQLite HTTP smoke suite, including retries, repeated taps, partial payment, paid removal, stale-token rejection, later additions at changed prices, idempotent delivery, closure, read-only history and disabled categories. Existing NuGet warnings remain; SQL Server is not yet verified.

Next: active-order filters, paginated history and audit queries (steps 6–7), then the React application and expanded CI.

## 2026-10-05 — Steps 6–7: Active filters, history and audit queries

- Added unit-based undelivered/unpaid filters with AND semantics, paginated closed-order history and table filtering, plus authenticated actor/time audit queries.
- History uses stable ID pagination across providers; active orders and audit entries use chronological ordering. Documented the query contracts and this ordering distinction.
- Verified the full SQLite smoke suite with filter exclusions, history pagination/validation, immutable snapshots, audit counts and authenticated access. Every real unit mutation is audited; retries create no duplicate audit records.

Next: React operational and administration screens, then provider/concurrency verification and full CI integration.

## 2026-10-05 — Step 8: React operational application

- Added the React/TypeScript/Vite application with Czech sign-in, role-aware navigation, table tiles, direct-category/subcategory ordering, active filters, kitchen quantities/times, payment, removed-unit visibility, closed history and named audit entries.
- Added per-group quantity controls and individual-unit checkboxes, selected and whole-order actions, payment amount confirmation, guarded corrections, server-owned totals, periodic refresh and conflict recovery.
- Added a sequential tap queue with per-tap UUIDs and session-storage retry recovery. Repeated taps remain separate units. Added responsive layouts, 48px controls and keyboard focus styles without runtime CDN dependencies.
- Verified TypeScript/production build, formatting and unit-grouping tests. Browser phone-flow verification remains for the end-to-end step. npm installation reports no vulnerabilities.
- Pinned exact frontend dependencies and documented local development. Administration screens and integrated deployment/CI are next.

## 2026-10-05 — Step 9: Administration and phone end-to-end verification

- Added administrator forms for tables, categories, subcategories, items, enabled states, sorting and accounts; bulk subcategory assignment; CSV template/file upload, UTF-8/Windows-1250 decoding, preview errors and import summary.
- Added an explicit API `--migrate` command for setup and isolated test fixtures.
- Added Playwright phone tests backed by a temporary SQLite database and real API. Verified administrator table/CSV/account setup, staff-only navigation, repeated touch additions, reopening the order, selected delivery/payment, kitchen quantities, bulk completion, closed history and named audit entries.
- Both 390×844 and 360×800 touch viewports passed; screenshots show no horizontal overflow and no browser runtime errors. Production build and formatting checks passed. In-app browser execution tools were unavailable, so the browser verification uses standalone Playwright.

Next: SQLite/SQL Server concurrency matrix, integrated frontend/API artifact, full TeamCity checks and deployment/staff documentation.
