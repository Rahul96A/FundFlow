# FundFlow web app

React 19 + TypeScript (strict) + Vite + MUI 9. Node 22+. Feature-based structure; see [docs/conventions.md](../docs/conventions.md#frontend-react--typescript).

```bash
npm ci               # (npm install when changing dependencies)
npm run dev          # http://localhost:5173, proxies /api to http://127.0.0.1:5080 (override with VITE_API_PROXY_TARGET)
npm run lint         # oxlint (correctness + a11y + hooks rules)
npm run typecheck    # tsc -b
npm test             # Vitest, one run (npm run test:watch to watch, npm run test:coverage for coverage)
npm run build        # type-check + production bundle in dist/
npm run e2e          # Playwright against E2E_BASE_URL (default http://localhost:5173); needs the full stack running
                     # E2E_BROWSER_CHANNEL=msedge (or chrome) uses an installed browser instead of downloading Chromium
```

MUI 9 note: system props (`alignItems`, `justifyContent`, `mt`…) are no longer accepted on `Stack`, `Box`, `Grid` or `Typography`; use
`sx`. Input internals go through `slotProps` (`slotProps.input`, `slotProps.htmlInput`), and `Typography color` takes palette keys
(`textSecondary`), not `text.secondary`.

## Layout

```
src/
  app/            App, providers (query client, theme, toasts), router (routes + guards), stores (auth, ui), theme
  components/
    ui/           Design system: PageHeader, StatCard, StatusBadge, EmptyState/LoadingState/ErrorState, Modal, Drawer,
                  ConfirmDialog (+ imperative useConfirm), Toast, SearchBox, DateRangePicker, ChartCard, Timeline, ActivityFeed, Logo
    layout/       AppShell, Sidebar (collapsible, responsive), TopNavigation, AppBreadcrumbs, navigation config
    forms/        React Hook Form bound fields (FormTextField, PasswordField, FormSelect, FormCheckbox, FormField)
    tables/       DataTable (server-side paging/sort, selection, bulk actions, column visibility, CSV export), FilterBar
  features/       auth · dashboard · organization · users · roles · audit · account · platform · errors
                  (each: api/ components/ hooks/ pages/ schemas/ types/ as needed)
  shared/         api (axios client with refresh, problem-details parsing, query client), constants, hooks, types, utils
  test/           Vitest setup and render helpers
e2e/              Playwright specs (self-contained: registers an organization and reads email from Mailpit)
```

## How the API is called

* `shared/api/http.ts` attaches the in-memory access token, and on a 401 renews it **once** using the HttpOnly refresh cookie
  (single-flight across concurrent requests and serialised across tabs with a Web Lock), then retries the request. If renewal
  fails the session is cleared and route guards send the user to sign in.
* Errors are RFC 7807; `shared/api/problem.ts` turns them into `ApiError` (status, code, field errors, trace id) and maps field
  errors onto forms.
* Table state (page, sort, search, filters) lives in the URL (`useTableParams`).

The browser only talks to its own origin: the Vite dev server (or nginx in Docker) proxies `/api`, which keeps the refresh cookie
first-party (`SameSite=Strict`).
