# CSV import

All `/catalog/import` endpoints require an Administrator session. POST requests
also require `X-CSRF-TOKEN` from `/auth/csrf`. See [authentication](authentication.md).

## File format

Download `GET /catalog/import/template` for a UTF-8 CSV example. Decode uploaded
files as UTF-8 (optional BOM); send the decoded text to the preview endpoint.
The delimiter is a comma and the header must contain these columns in this order:

```csv
CISMAT,NAZMAT,DRUMAT2,PROCEN5,SAZDPH
001,"Káva, velká",A,41.32,21
```

- `CISMAT`: stable item code, 1–50 characters after trimming; leading zeroes and letters remain intact.
- `NAZMAT`: item name, 1–200 characters after trimming.
- `DRUMAT2`: category code, 1–50 characters after trimming.
- `PROCEN5`: nonnegative price before VAT, at most two decimal places, maximum 4999999999999999.99.
- `SAZDPH`: VAT percentage, 0–100, at most two decimal places.

Use a decimal point without thousands separators. Enclose names containing commas
or newlines in double quotes; escape a quote as `""`. Empty rows are ignored.
`emptyRows` counts empty CSV records such as `,,,,`; physically blank lines are
skipped by the parser. Errors identify the physical starting line of each record,
including the header as line 1. Malformed quoted records are rejected.

The supplied file has 201 populated records and nine empty CSV records. Its
category codes have 13 case-sensitive spellings, representing 11 categories when
`k`/`K` and `z`/`Z` are matched ignoring case. Import matches item and category codes
ignoring case, preserving the spelling of existing records. Every otherwise-valid
occurrence of a duplicated item code in the upload is rejected; no first/last row wins.
Existing ambiguous case variants cause a 409 and roll back the entire import.
Use ASCII codes for consistent case matching across database providers; SQL Server
collation can additionally equate accented variants.

## Preview and confirmation

1. `POST /catalog/import/preview` with JSON `{ "csv": "CISMAT,...\n..." }`.
   The maximum CSV length is 1,000,000 characters. Response: `rows`, `errors`,
   `emptyRows`, `token`, and `expiresAt`. Each valid row contains `row`, `code`,
   `name`, `categoryCode`, `priceBeforeVat`, `vatRate`, and calculated `price`.
   Each error contains `row` and a Czech `message`. Preview changes no data.
2. Show valid rows and rejected-row errors to the administrator. Confirmation
   imports **only valid rows**, even if the file also contains errors.
3. `POST /catalog/import/commit` with JSON `{ "token": "<preview token>" }`.
   Response: `{ "created": 201, "updated": 0, "categoriesCreated": 11 }`
   (counts depend on the existing catalog).

The protected token binds the validated rows to the administrator account and
expires after 30 minutes. It is null when no rows are valid. The server does not
accept edited row data at commit. Invalid, tampered, expired, or other-account
tokens return 400 `invalid_import_token`. An invalid preview input returns 400
`invalid_csv`. Catalog conflicts return 409 `import_conflict` or
`ambiguous_import_code`; no partial changes persist. Keep Data Protection keys
persistent/shared when deploying multiple API instances, as for login cookies.

## Update rules

Commit runs within one serializable database transaction. New categories use their
code as the initial display name; administrators can rename and sort them later.
New items are enabled with no subcategory. Matching items keep their ID, enabled
state, code spelling, and sort order; import updates their name, category, source
price, VAT rate, and ordering price. Changing category clears the old subcategory;
keeping the category preserves the assignment. Existing category names, sort order,
and enabled state are preserved. Disabled records are not re-enabled by import.

Ordering price is `PROCEN5 × (1 + SAZDPH / 100)`, rounded to two decimal places
with midpoint rounding away from zero (10.05 at 10% becomes 11.06).
Historical order-unit snapshots are never modified. Use the existing
`POST /catalog/items/assign-subcategory` API for optional bulk assignment afterward.

A token can be retried before expiry: rows match existing codes rather than creating
duplicates. A retry reapplies its preview values to the current catalog; the preview
does not lock the catalog or protect intervening manual edits from being replaced.

Run the SQLite verification suite from the repository root:

```powershell
dotnet run --project backend/RestaurantOrders.SmokeTests
```

This verifies the supplied file, quoting, validation, permissions, preview isolation,
token tampering, updates, category changes, repeated import, and transaction rollback.
SQL Server execution remains pending an isolated test instance.
