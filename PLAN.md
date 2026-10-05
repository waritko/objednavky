# Restaurant ordering system: implementation plan

## 1. Scope and agreed behavior

Build a Czech-language web application for restaurant staff. It must work well on a standard mobile phone, with large touch targets and minimal keyboard use during service.

- A waiter selects a table and adds menu items by tapping an item once per unit. The current order remains visible during ordering.
- A table can have one active order. Selecting a table with an active order opens it for additions; otherwise the application creates a new order.
- Menu items belong to a category and subcategory and have a name and price.
- Kitchen staff can see items that have not been delivered to the customer table.
- Each ordered unit can be marked **Processed** separately. Processed means **delivered to the customer table**, not merely prepared. An action on the whole order marks all remaining units as Processed.
- Guests may pay for part of an order. Each item, including a selected quantity of identical items, can be marked **Paid** separately. An action on the whole order marks all remaining units as Paid.
- Active orders can be filtered by **Nevydané** (at least one unit not Processed) and **Nezaplacené** (at least one unit not Paid).
- Newly added units start as not Processed and not Paid, including units added to an existing order.
- Administrators have separate login and access to table and menu management. Multiple operational accounts are supported; each can use both waiter and kitchen features. A shared phone remains signed in to one operational account during use, with no in-app account-switching flow.
- Historical orders remain stored indefinitely.

## 2. Decisions to confirm before implementing the affected workflow

The specification does not settle these details. Use the proposed behavior below unless product review changes it.

| Topic | Proposed behavior | Why it matters |
| --- | --- | --- |
| Closing an order | Close it when every unit is both Processed and Paid. A closed order is read-only and the table can start a new order. | Defines table reuse and what appears in the active-order list. |
| Removing or changing units | Allow removal of an unpaid unit; require a separate correction or refund process for a paid unit. Keep an audit record of removals. | Prevents paid history from being silently rewritten. |
| Preparation progress | Use only the specified Processed state for delivery. The kitchen sees undelivered units; there is no separate “ready” state in the first release. | Avoids inventing a preparation workflow. |
| Payment handling | Record that units were paid, with time and acting account; do not integrate a payment terminal or accounting system in the first release. | The specification describes payment status, not payment collection. |
| Authentication | Use administrator and operational roles, with named accounts and password-based login. Accounts stay signed in until logout or session expiry. | Establishes access control and shared-device behavior. |
| CSV import | Provide a documented UTF-8 CSV template with category, subcategory, name, and price; preview and validate before applying. | The input format is not specified. |

These are implementation choices, not additional confirmed requirements. If closing must be manual or paid items must be refundable in the first release, adjust the order lifecycle and tests before building the payment screen.

## 3. Architecture and repository structure

- **Backend:** ASP.NET Core HTTP API in C#. Keep business rules in application services rather than controllers.
- **Frontend:** React with TypeScript. Use responsive layouts, Czech interface text, and accessible touch controls.
- **Persistence:** Entity Framework Core with SQLite as the default provider and SQL Server as a selectable provider. Keep provider-specific configuration and migrations isolated. Avoid provider-specific SQL in order logic.
- **Authentication:** Server-managed accounts and authorization for `Administrator` and `Operational` roles. Protect administration endpoints on the server; the frontend also hides inaccessible navigation.
- **Suggested layout:** `backend/` for API, domain/application code, persistence, and tests; `frontend/` for the React app and tests; `docs/` for CSV and deployment guidance; TeamCity configuration in the repository.
- **Deployment configuration:** Connection string, database provider, session settings, and administrator bootstrap values come from configuration or environment variables, never source-controlled secrets.

Use one API contract shared by the mobile screens and administration. Document request and response shapes and error codes as the endpoints are added.

## 4. Data model and invariants

### Main records

| Record | Essential fields and purpose |
| --- | --- |
| Account | Identifier, login name, password hash, role, enabled flag. |
| Table | Identifier, display name/number, sort order, enabled flag. Disabling preserves historical references. |
| Category | Identifier, name, sort order, enabled flag. |
| Subcategory | Identifier, parent category, name, sort order, enabled flag. |
| Menu item | Identifier, subcategory, name, current price, sort order, enabled flag. |
| Order | Identifier, table, opened/closed times, lifecycle state, creator, concurrency token. |
| Order unit | Identifier, order, source menu item, snapshot of item/category names and unit price, added time/account, Processed time/account, Paid time/account, optional removal time/account. |
| Audit event | Order, actor, timestamp, action, affected unit(s), and relevant before/after values for corrections. |

Store **one order unit per tap**. The UI may group identical units and display a quantity, but each unit retains independent Processed and Paid states. This supports selected-quantity actions without rounding or ambiguity. Prices and names are copied into the order when a unit is added so later catalog edits do not change historical receipts or totals. Use decimal money values, a consistent currency setting, and explicit rounding rules.

### Rules enforced by the backend

- At most one active order exists per table, enforced by a database constraint or a transaction-safe equivalent on both providers.
- Adding units to a table is atomic: find its active order or create one, then append units at their current menu prices.
- A new unit is unpaid and unprocessed even when other units in the order are complete.
- Bulk actions affect only eligible units and return the resulting state; repeated requests must not duplicate effects.
- Order totals derive from non-removed units. Paid and unpaid totals derive from each unit's Paid state.
- Closed orders and historical snapshots remain queryable. Normal administration disables referenced catalog records instead of deleting them.
- Concurrent changes from two devices must not lose units or overwrite status changes. Use transactions and concurrency checks where needed, returning a clear conflict response to the client.

## 5. Backend work

1. Create the API project, persistence projects/configuration, database migrations, validation, error handling, and health endpoint.
2. Implement account setup, login/logout, session handling, password hashing, and role authorization. Provide a secure first-administrator bootstrap procedure and account administration.
3. Implement table and catalog CRUD, ordering/sorting, enabled state, and price validation.
4. Implement CSV upload as a two-step flow: parse and preview validation results, then commit valid rows in a transaction. Report row numbers and reasons for rejected rows. Define duplicate and update behavior in the CSV guide before enabling import.
5. Implement table/order queries, atomic order creation and additions, unit removal/correction rules, per-unit Processed and Paid actions, and bulk Processed/Paid actions.
6. Implement active-order filters and historical queries. Filter semantics use the presence of at least one matching unit, rather than an order-wide flag.
7. Add audit entries for status changes, additions, and corrections. Expose actor/time information where staff need it.

Suggested endpoint groups are `/auth`, `/accounts`, `/tables`, `/catalog/categories`, `/catalog/items`, `/catalog/import`, and `/orders`. Keep all state-changing order operations server-side; do not calculate authoritative totals or closure solely in the browser.

## 6. Frontend work

### Operational screens

1. **Sign-in and home:** Simple Czech login. Present table/order and kitchen navigation to operational accounts. No account-switch control within the service workflow.
2. **Table selection:** Large table tiles showing active-order state, amount unpaid, and undelivered count. Make returning to a table fast.
3. **Ordering:** Category and subcategory navigation, large item buttons, one tap per unit, visible quantity feedback, and a persistent current-order list and total. Provide clear undo/removal for accidental unpaid taps.
4. **Active orders:** Cards with table, totals, outstanding counts, and `Nevydané` / `Nezaplacené` filters. Open an order for edits or status changes.
5. **Kitchen:** List undelivered units, grouped by table and order with readable quantities and addition times. Allow a specific unit or selected quantity to be marked Processed; provide the specified whole-order action.
6. **Payment:** Show grouped items with paid/unpaid quantities, allow selection of individual units or a quantity, show the amount being marked paid, then require a clear confirmation. Include the whole-order Paid action.
7. **History:** Read-only completed orders with original item names, prices, and status timestamps.

### Administration screens

- Manage tables, categories, subcategories, menu items, prices, and enabled state.
- Import CSV with template download, preview, row-level errors, and a result summary.
- Manage operational accounts and administrator accounts according to role permissions.

Use Czech text throughout, including validation, errors, empty states, and confirmations. Test with a phone-width viewport and touch input; avoid hover-only controls and routine free-text entry during service.

## 7. Verification

### Backend and database

- Unit-test order totals, active-order filter predicates, status transitions, and CSV validation.
- Run the same integration scenarios against **SQLite and SQL Server**: table/order creation, concurrent additions, selected-quantity Processed and Paid actions, bulk actions, later additions, catalog price changes, removal rules, and closure.
- Apply migrations to fresh databases for each provider and verify historical snapshot queries after catalog changes.
- Use an isolated SQL Server test instance in CI; never make integration tests depend on production data.

### Frontend and end-to-end

- Verify the main touch flow: sign in, select a table, tap an item repeatedly, add to an existing order, deliver one of several identical units, pay a selected quantity, and view the remaining unpaid/undelivered units.
- Verify administration access is denied to operational accounts at both UI and API levels.
- Check common phone viewport sizes, touch targets, Czech text wrapping, keyboard accessibility, loading/error feedback, and double-tap/retry behavior.

### TeamCity pipeline

1. Restore backend and frontend dependencies; run formatting/lint and type checks.
2. Build the API and frontend.
3. Run backend unit tests and frontend tests.
4. Start isolated SQLite/SQL Server integration test environments and run the provider matrix.
5. Run a short end-to-end smoke test and publish test results/build artifacts.

Record exact SDK, runtime, database, and Node versions in the project once implementation begins so local and TeamCity builds match.

## 8. Delivery sequence and acceptance gates

| Stage | Deliverable | Acceptance gate |
| --- | --- | --- |
| 1. Foundation | Repository structure, API/React builds, migrations, TeamCity build, login and roles. | Administrator and operational accounts sign in; unauthorized administration calls fail; both database providers migrate. |
| 2. Catalog | Table and menu administration plus CSV import. | An administrator can configure a complete menu and tables; invalid CSV rows get clear errors; historical references remain safe. |
| 3. Ordering | Mobile table picker and ordering flow. | Repeated taps add separate units; an active table order reopens; totals use saved unit prices. |
| 4. Service and payment | Kitchen view, active filters, per-unit and bulk status actions, partial payment. | Identical units can have different delivery/payment states; newly added units are outstanding; filters and totals remain correct. |
| 5. History and hardening | Closure, history, audit, concurrency handling, documentation, full CI. | Both provider suites and phone flow pass; closed orders remain readable and the table can accept a new order. |

## 9. Documentation to deliver with the implementation

- Local setup for SQLite and SQL Server, including migrations and test commands.
- Administrator bootstrap and account management instructions.
- CSV template, encoding, price format, duplicate handling, and import error rules.
- TeamCity setup and required secret/configuration values.
- Short Czech staff guide for ordering, delivery, partial payment, and correcting an accidental tap.
