# Czech staff application

React + TypeScript, built with Vite. Dependency versions are exact and locked in `package-lock.json`. The initial build was verified with Node 25.9.0 and npm 11.12.1; CI pins the same versions.

```sh
npm ci
npm run dev
npm run build
npm test
npm run format:check
```

Run the API at `http://127.0.0.1:5080`, or set `API_URL` before starting Vite. Open the displayed Vite address. Requests are proxied to the API on the same browser origin so authentication and CSRF cookies work without CORS.

The operational interface includes tables, ordering, active orders, kitchen, payment and history. Each tap receives a stable UUID; additions queue sequentially and unresolved additions remain in session storage for explicit retry. Do not clear waiting additions before checking the server's order. Status conflicts reload the order and require renewed selection. The server owns all totals and closure decisions. Lists refresh every ten seconds when idle.

No external fonts, analytics or CDN scripts are required. Touch targets are at least 48 pixels; individual-unit selection is also available through keyboard-accessible details controls.
