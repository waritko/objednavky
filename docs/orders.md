# Orders API

All endpoints require a signed-in account; writes also require `X-CSRF-TOKEN`.

- `GET /tables/{id}/order`: active order, or 204 if none exists. Empty table selection does not create a stored order; the first tap creates it atomically.
- `POST /tables/{id}/units`: `{menuItemId, unitId}`. Generate a UUID once for each tap and retain it for network retries. Reusing it for the same item/table/account returns the existing order, even after closure, without adding another unit. A different payload returns 409.
- `GET /orders/{id}`: active or closed order including removed units and immutable snapshots.
- `GET /orders?undelivered=true&unpaid=true`: active orders having at least one non-removed unit matching each requested condition; combined filters use AND, and may match different units. Oldest opened orders first.
- `GET /orders/history?page=1&pageSize=25&tableId={optional UUID}`: closed orders, total count and pagination. Page size 1–100; latest unit payment date/time first, sorted before pagination across both providers. Orders without payments come last; ties use closing time (newest first), then order ID.
- `GET /orders/{id}/audit`: chronological changes with actor ID, current username, timestamp, affected unit and JSON before/after details. Requires authentication, including historical orders.
- `POST /orders/{id}/note`: `{concurrencyToken, note, menuItemId?}`. Omit `menuItemId` for the order note; include it for the shared note on all quantities of that item code. Notes are trimmed, limited to 1000 characters, and cleared with null or blank text. Only active orders and non-removed item lines can be edited. Changes create `NoteChanged` audit events with item ID and before/after text. Responses include `note` and `lineNotes` (a dictionary keyed by menu item ID), also in kitchen listings and history.
- `POST /orders/{id}/processed`, `/paid`, `/removed`: `{concurrencyToken, unitIds}` for selected units, or `{concurrencyToken, all: true}` for the entire order. Paid removals additionally require `confirmPaidRemoval: true`.

Responses include `id`, `tableId`, `state` (`Active`/`Closed`), `concurrencyToken`, opened/closed timestamps, `total`, `paid`, `unpaid`, outstanding counts and individual `units`. Unit records include snapshot names/prices and account IDs/timestamps for addition, payment, delivery and removal. Totals exclude removed units. Removal preserves payment and creates no refund.

After a successful payment in the staff interface, the browser automatically downloads
`platba-{table name}-{YYYY-MM-DD_HH-mm-ss-SSS}.csv`, using local date/time and
replacing filename-unsafe characters in the table name with underscores.
This works for both selected-unit and
whole-table payments, even when payment does not yet close the order. The UTF-8 CSV
(with BOM, comma separators and CRLF records) has `product_code,product_name,quantity`
columns. Product code is the catalog item's code (including leading zeroes); product name is the
saved order-unit name. Quantities count only units newly paid by that action,
grouped by item and saved name. Already paid and removed units are excluded.
Cancelled or rejected payments produce no download.

Use the latest concurrency token for status changes. A stale token or concurrent database write returns 409 `order_conflict`; reload before offering the action again. An uncertain addition may safely retry the same unit ID. Repeated status actions with a fresh token affect only eligible units. Closed orders reject edits with 409 `order_closed`. Errors include Czech `message` text. Selecting a foreign unit rejects the entire action.

Writes use serializable database transactions, a unique active-table index and optimistic order tokens. Each real unit change writes an audit event in the same transaction. When every non-removed unit is paid and delivered (including removal of the final unit), the order closes and releases its table. Disabled tables/items/categories/subcategories cannot receive additions; existing units remain serviceable.

## Daily sales (administrator only)

`GET /orders/sales` returns seven calendar days, newest first: today and the
previous six days in `Europe/Prague`. Each day contains `date`, `quantity`,
`total`, and `items` (`menuItemId`, historical `itemName`, `quantity`, `total`).
Days with no payments are included with zero totals and an empty item list.

Sales are assigned to the payment date (`PaidAt`), including paid units from
active and closed orders. Unpaid units are excluded. Paid units subsequently
removed remain included because removal does not refund their payment.
Totals use the saved unit prices, including VAT, rather than current menu prices.
The UI is available to administrators through **Prodeje**, with daily selection
and a refresh button. Anonymous requests receive 401; operational accounts receive 403.
