# Table and catalog API

All routes require a signed-in account. GET is available to both roles. POST and
PUT require `Administrator` and the CSRF header described in authentication.md.
Lists include disabled records so clients can display historical references and
administrators can re-enable them. Ordering clients must exclude disabled tables,
items, categories, and (when assigned) subcategories.

| Collection | POST / PUT body |
| --- | --- |
| `/tables` | `name`, `sortOrder`, `enabled` |
| `/catalog/categories` | `code`, `name`, `sortOrder`, `enabled` |
| `/catalog/subcategories` | `categoryId`, `name`, `sortOrder`, `enabled` |
| `/catalog/items` | `code`, `categoryId`, nullable `subcategoryId`, `name`, `priceBeforeVat`, `vatRate`, `sortOrder`, `enabled` |

GET the collection to read all records, POST to create, and PUT `/{id}` to replace
editable fields. Successful writes return HTTP 200 with the saved record. Records
include `id`; items also include the server-calculated `price`. PUT is a full
replacement: omitted `sortOrder` defaults to 0, `enabled` to true, and
`subcategoryId` to null. Lists sort by sortOrder, name, then ID. Disable via PUT
with `enabled: false`; there is no destructive DELETE endpoint.

Names and codes are trimmed. Codes retain letters and leading zeroes. Category
and item codes must be unique within their collection. Name limits are 100 for
tables, 150 for categories/subcategories, and 200 for items; codes allow 50
characters. Sort order is a signed 32-bit integer.

Prices use decimal arithmetic: `priceBeforeVat * (1 + vatRate / 100)`, rounded to
two places with midpoint away from zero. Source price and VAT accept at most two
decimal places, with source price between 0 and 4999999999999999.99 and VAT between
0 and 100. These bounds fit both providers' decimal columns. Price is never
accepted from the client as authoritative.

Subcategories must exist in the item's category. An assigned subcategory cannot
be moved to another category until its items are unassigned. To move an item,
supply its new category and either a matching subcategory or null. Disabled
parents may be edited; disabling a parent does not rewrite its children's flags.
Catalog changes never rewrite existing order-unit snapshots.

POST `/catalog/items/assign-subcategory` accepts
`{"itemIds":["guid"],"subcategoryId":"guid-or-null"}` (use JSON null to clear).
Select 1–500 IDs. Duplicates are ignored. Every item must exist and, for assignment,
belong to the target subcategory's category. Validation and updates are atomic.

Errors return `{code, message}` with Czech messages: HTTP 400
`invalid_catalog_input`, `invalid_price`, `invalid_category`,
`invalid_subcategory`, `invalid_selection`; HTTP 409 `code_exists`,
`subcategory_in_use`, `catalog_conflict`. Missing records return 404;
authentication and role failures return 401/403. All writes use serializable
transactions; constraint failures return a conflict that clients can resolve by
reloading. The planned order service must independently enforce orderability.

Run the authentication and catalog HTTP checks against a temporary, freshly
migrated SQLite database with:

```powershell
dotnet run --project backend/RestaurantOrders.SmokeTests
```

SQL Server execution remains pending an isolated test instance.
