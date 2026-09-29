# Development guide

## Prerequisites

| Tool | Version | Used for |
| --- | --- | --- |
| .NET SDK | 10.0 (pinned by `global.json`) | Backend |
| Node.js | 22 LTS | Frontend |
| Docker | Desktop or Engine with Compose v2 | Infrastructure and integration tests |

## Two ways to run

### A · Everything in Docker

```bash
cp .env.example .env
docker compose up -d --build
```

Web <http://localhost:3000> · API <http://localhost:5080/swagger> · Mailpit <http://localhost:8025>. The API container runs in the
`Development` environment: it applies migrations and seeds the demo tenants on first start.

### B · Dependencies in Docker, apps on the host (recommended for daily work)

```bash
docker compose up -d sqlserver sqlserver-init redis rabbitmq mailpit
dotnet run --project src/FundFlow.Api          # http://localhost:5080
cd frontend && npm install && npm run dev      # http://localhost:5173
```

`appsettings.Development.json` already points at the compose services on their default ports, with the development secrets from
`.env.example`. If you changed a port or password in `.env`, override the setting with an environment variable or user secrets:

```bash
export ConnectionStrings__Default="Server=127.0.0.1,1433;Database=FundFlow;User Id=sa;Password=…;TrustServerCertificate=True"
export ConnectionStrings__Redis="127.0.0.1:6380,password=…,abortConnect=false"
dotnet user-secrets --project src/FundFlow.Api set "Jwt:SigningKey" "$(openssl rand -base64 48)"
```

## Environments and secrets

| Environment | Purpose | Notes |
| --- | --- | --- |
| `Development` | Local | Swagger on, auto-migrate + demo seed, Hangfire dashboard, human-readable logs |
| `Testing` | Integration tests | Set by the test host; in-memory transport/cache, rate limits off |
| `Staging` / `Production` | Deployed | Swagger off, JSON logs, **explicit `--migrate` step**, secrets from the platform's secret store |

Layering: `appsettings.json` → `appsettings.{Environment}.json` → environment variables (`Section__Key`) → user secrets (Development).
**Never commit** real secrets. `.env` is git-ignored; the dev signing key is refused outside Development/Testing.

## Testing

```bash
dotnet test tests/FundFlow.Domain.Tests                 # 79 tests, milliseconds: entities & business rules
dotnet test tests/FundFlow.Application.Tests            # 102 tests: validation, security services, tenant provider, paging
dotnet test tests/FundFlow.Api.IntegrationTests         # ~130 tests, ~6 min first time; needs Docker
cd frontend && npm test                                 # 136 Vitest tests (components, guards, stores, utilities)
cd frontend && npx playwright install chromium && npm run e2e   # 8 browser tests against the running stack
```

* **Integration tests** start a SQL Server container (Testcontainers), boot the *real* API on it (real migrations, real MassTransit outbox with
  an in-memory transport, real JWT auth) and create state only through the public API. They cover authentication flows and races, permission
  matrices, privilege escalation, the last-administrator guard, **tenant isolation** (API and data layer), audit, ProblemDetails,
  pagination, security headers, rate limiting, OpenAPI generation, health checks, and "model matches migrations".
* Filter while iterating: `dotnet test --filter "FullyQualifiedName~TenantIsolation"`.
* The first integration run pulls the SQL Server image (~1.5 GB). Set `TESTCONTAINERS_RYUK_DISABLED=true` only if your environment forbids the reaper container.
* Coverage: `dotnet test --collect:"XPlat Code Coverage"`; frontend `npm run test:coverage`.
* **Browser tests** register a new organization through the UI, read the verification email from Mailpit, sign in, invite a
  colleague, accept the invitation, reset a password, and check the role-limited menu and the phone layout. Point them at the
  Docker stack with `E2E_BASE_URL=http://127.0.0.1:3000 E2E_MAILPIT_URL=http://127.0.0.1:8025`. To use a browser you already have
  instead of downloading Chromium, set `E2E_BROWSER_CHANNEL=msedge` (or `chrome`).
* The single-origin build that ships to Azure (API serving the React app from `wwwroot`) is covered by `SpaHostingTests`; to try it
  by hand, copy `frontend/dist` into the published API's `wwwroot` and run it with `ASPNETCORE_ENVIRONMENT=Production`.

## Common tasks

| Task | Command |
| --- | --- |
| New migration | see [database.md](database.md#commands) |
| Reset the local database | `docker compose down -v && docker compose up -d` |
| Read the emails the app sent | <http://localhost:8025> |
| Inspect the outbox / bus | RabbitMQ UI <http://localhost:15672>; table `messaging.OutboxMessage` |
| Tail API logs | `docker compose logs -f api` (JSON outside Development) |
| Regenerate the OpenAPI document | `curl http://localhost:5080/swagger/v1/swagger.json` |
| Add a permission | edit `Domain/Identity/Permissions.cs` (+ `RoleTemplates`), run the API (dev auto-syncs) or `--migrate` |

## Troubleshooting

* **SQL Server connection times out on Windows with `localhost`.** `localhost` resolves to IPv6 `::1` first and Docker Desktop does not
  proxy IPv6, so the connect hangs until the timeout. Use `127.0.0.1` (the development settings already do).
* **Port already in use (6379, 1433, 5672…).** Another project's container owns it. Change `*_PORT` in `.env` (e.g. `REDIS_PORT=6380`)
  and point the matching setting at it.
* **`docker compose up` says a variable is required.** Copy `.env.example` to `.env`.
* **API stays unhealthy on first start.** The first migration on a cold SQL Server takes a while; `docker compose logs api` shows progress.
  The health check allows 40 s of start-up; slow disks can need more.
* **Integration tests fail with "Docker is not running".** Start Docker Desktop; the tests need a daemon.
* **npm install is slow / times out on locked-down networks.** Point npm at your mirror: `npm config set registry <url>`.
* **Refresh cookie not sent by the SPA.** The SPA and API must share an origin (Vite proxy, nginx). Don't call the API directly from
  another origin in the browser: the cookie is `SameSite=Strict`.
* **Emails don't arrive.** They are delivered by a background consumer a second or two after the request; check the API log for
  `send-transactional-email` errors and that RabbitMQ is healthy. In tests the transport is in-memory.

## Windows notes

* Line endings are LF (`.editorconfig`). Configure `git config core.autocrlf false` (or `input`) so shell scripts and Dockerfiles keep LF.
* Git Bash rewrites paths that start with `/` when passed to `docker exec`; prefix the command with `MSYS_NO_PATHCONV=1`.
