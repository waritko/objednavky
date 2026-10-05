# Accounts and sessions

Apply the database migration before starting the API (see `backend/README.md`).
Create the first administrator by setting `Bootstrap__Username` and
`Bootstrap__Password` through environment variables or local user secrets, then run:

```powershell
dotnet run --project backend/RestaurantOrders.Api -- --bootstrap-admin
```

The command exits after creating the account and refuses to run when any account
already exists. Remove the bootstrap credentials from the environment afterward.
Usernames are trimmed, converted to lowercase, and limited to 100 ASCII letters,
digits, dots, hyphens, or underscores. Passwords must contain 12–256 characters.
Passwords use ASP.NET Core Identity's salted password hasher; responses never
include hashes. Only administrators may list, create, or update accounts.
The last enabled administrator cannot be disabled or demoted.

## HTTP contract

All requests and responses use JSON. Account responses contain `id`, `username`,
`role` (`Administrator` or `Operational`), and `enabled`.

| Method and path | Request | Result |
| --- | --- | --- |
| GET `/auth/csrf` | None | `{ "token": "..." }` and an antiforgery cookie |
| POST `/auth/login` | `username`, `password` | Account and session cookie |
| GET `/auth/me` | None | Current account; 401 without a valid session |
| POST `/auth/logout` | None | 204; deletes the session cookie |
| GET `/accounts` | None | Account array; administrator only |
| POST `/accounts` | `username`, `password`, `role`, optional `enabled` (default true) | 200 account |
| PUT `/accounts/{id}` | `username`, `role`, `enabled`, optional `password` | 200 account; 404 for unknown ID |

PUT replaces account settings; omit `password` or send null to retain the existing
password. Accounts are disabled rather than deleted to preserve history.

Send cookies on requests. Before login, obtain `/auth/csrf` and send its token in
the `X-CSRF-TOKEN` header on every unsafe request, including login and PUT.
Fetch a new token after login because tokens are tied to the current identity.
Clients must use the same origin as the API; cookies use SameSite Strict.

Invalid input returns 400 with `{ "code": "...", "message": "Czech message" }`.
Codes include `invalid_username`, `invalid_password`, `invalid_role`, and
`invalid_csrf`. Invalid login returns 401 `invalid_credentials`. Duplicate names,
last-administrator changes, and conflicting writes return 409 (`username_exists`,
`last_administrator`, `account_conflict`). Authorization returns 401 or 403 with
no redirect. Login is limited to 10 attempts per remote IP per minute (429).

Sessions persist across browser restarts and expire after `Session__Hours`
(default 12), with sliding renewal. Disabled accounts are rejected on the next
request. Changing a username, password, or role invalidates existing sessions.
Logout clears the current browser's session cookie. Production cookies require
HTTPS. Persist and protect ASP.NET Core Data Protection keys in deployment so
restarts retain valid sessions; share keys across instances of the same app.

## Verification

```powershell
dotnet run --project backend/RestaurantOrders.SmokeTests
```

The executable smoke suite applies SQLite migrations to a unique temporary
database and starts the API on an available loopback port. It checks bootstrap,
both roles, authorization, CSRF, duplicate usernames, last-administrator protection,
password reset, disabling, session invalidation, and logout. It exits nonzero on
failure and removes its test database. See [verification](testing.md) for the same suite on isolated SQL Server and the phone browser tests.
