# Infrastructure: SQL Server, Redis, RabbitMQ, containers

Local development runs everything with `docker compose up` (see [development.md](development.md)). This page explains what each
service is for and how it is configured; the config files live under [`infra/`](../infra).

## Compose services

| Service | Image | Host port | Purpose |
| --- | --- | --- | --- |
| `sqlserver` | `mssql/server:2022-latest` | 1433 (127.0.0.1) | Primary database |
| `sqlserver-init` | same (one-shot) | — | Creates the `FundFlow` database, snapshot isolation and the least-privilege `fundflow_app` login |
| `redis` | `redis:7-alpine` | 6379 (127.0.0.1) | Distributed cache, session deny-list |
| `rabbitmq` | `rabbitmq:4-management-alpine` | 5672, 15672 (127.0.0.1) | Message broker for MassTransit |
| `mailpit` | `axllent/mailpit` | 1025 (SMTP), 8025 (UI) | Development inbox; nothing is ever delivered externally |
| `api` | built from `src/FundFlow.Api/Dockerfile` | 5080 | ASP.NET Core API |
| `frontend` | built from `frontend/Dockerfile` | 3000 | nginx serving the SPA and proxying `/api` |

All ports bind to `127.0.0.1` only. Secrets come from `.env` (copy `.env.example`; git-ignored). The API container waits for
`sqlserver-init` to complete and for Redis/RabbitMQ to be healthy; every stateful service has a health check.

## SQL Server (`infra/sqlserver/init.sql`)

* **Collation** `SQL_Latin1_General_CP1_CI_AS`: case-insensitive so email/slug lookups behave as users expect.
* **`READ_COMMITTED_SNAPSHOT ON` + `ALLOW_SNAPSHOT_ISOLATION ON`**: readers never block writers. Essential for a mix of long report
  queries and high-rate writes (donations, bids), and it makes the optimistic-concurrency (`rowversion`) checks cheap.
* **Least privilege:** the API connects as `fundflow_app`, not `sa`. In development it is `db_owner` so migrations can run at startup.
  In staging/production give the runtime login only `db_datareader` + `db_datawriter` and run `--migrate` under a separate deployment
  identity (see [database.md](database.md)).
* EF Core is configured with `EnableRetryOnFailure` (5 retries, 10 s max delay) and a 30 s command timeout. Connection pooling is the
  SqlClient default; size `Max Pool Size` per instance when scaling out.
* **Sizing notes for the stated scale** (10k organizations, 1M donors, 10M donations): every tenant table leads its indexes with
  `TenantId`; ids are sequential-friendly GUIDs; lists use projection + `AsNoTracking` + bounded pages; the audit table is indexed
  for its three access paths and is a candidate for partitioning by month once it passes ~100M rows.

## Redis (`infra/redis/redis.conf`)

Redis holds only **derived, expiring data**; losing it costs a cold cache, never data:

| Key pattern | Content | TTL |
| --- | --- | --- |
| `access:{userId}` | Effective permissions & roles | 2 min (invalidated on change) |
| `tenant:id:{id}` / `tenant:slug:{slug}` | Tenant directory entries (name, slug, status) | 60 s (invalidated on suspend/activate) |
| `session:revoked:{sid}` | Deny-list marker for revoked sessions | access-token lifetime + 5 min |

Configuration: password required (`--requirepass`), `maxmemory 256mb` with `volatile-lru` (evict only keys that have a TTL — all of
ours do), AOF `everysec` so the deny-list survives a restart, `FLUSHALL/FLUSHDB/DEBUG` disabled. The API connects with
`abortConnect=false` so it starts even if Redis is briefly down, and `CacheService` degrades to "miss" on any Redis error rather than
failing requests. Without a Redis connection string (tests, minimal runs) an in-memory cache is used.

## RabbitMQ (`infra/rabbitmq/rabbitmq.conf`)

* Topology is **declared by MassTransit** at startup (kebab-case endpoints, e.g. `send-transactional-email`); nothing is pre-provisioned.
* **Reliability comes from the SQL Server transactional outbox** (`messaging.OutboxMessage`), not from broker settings: a message is
  written in the same transaction as the business change, then delivered by MassTransit's delivery service; consumers use the inbox
  for idempotency. A broker outage delays delivery; it never loses or duplicates business effects.
* Retry: exponential (5 attempts, 1 s → 5 min); poison messages land in `<queue>_error`.
* Guardrails: memory watermark 0.5, 1 GB free-disk limit, 60 s heartbeat, dedicated user (no remote `guest`).
* `Messaging:Transport=InMemory` swaps the transport (used by the integration tests) while keeping the real outbox in front of it.

## Mailpit

`Email__Host=mailpit`, port 1025, no TLS. Everything the app sends appears at <http://localhost:8025>. In production set
`Email__Host/Port/Security=StartTls/Username/Password` to a real relay (SES, SendGrid SMTP, Postmark…).

## Container images

* **API** — multi-stage: restore on project files only (layer-cached), publish `Release`, run on `aspnet:10.0` as the unprivileged
  `app` user, with a `curl`-based `HEALTHCHECK` on `/health/live`. Build context is the repo root (central package management).
* **Frontend** — `node:22-alpine` builds; `nginx-unprivileged` (uid 101, port 8080) serves the static bundle with strict security
  headers (CSP `script-src 'self'`), immutable caching for hashed assets, `no-store` for `index.html`, SPA fallback, and the `/api` proxy.

## Kubernetes readiness

The images are stateless and probe-friendly, so a Deployment needs only: env/secret injection (`ConnectionStrings__*`,
`Jwt__SigningKey`, `RabbitMq__*`, `Email__*`), `livenessProbe: /health/live`, `readinessProbe: /health/ready`,
a `Job` running `dotnet FundFlow.Api.dll --migrate` before rollout, and an Ingress terminating TLS with
`Proxy__TrustForwardedHeaders=true`. Data Protection keys live in the database, so replicas share them without a volume.
Helm charts / manifests are planned for Phase 8.

## Azure free tier

`infra/azure` deploys the whole product to Azure without a paid resource: an App Service F1 site that serves the API *and* the
React app (`Spa:Enabled`, `wwwroot`), plus the Azure SQL free-offer database. Redis and RabbitMQ are replaced by the in-process
cache and MassTransit's in-memory transport behind the same SQL outbox, and Hangfire is off. It is a single-instance demo/pilot
topology with real limits; see [deployment-azure.md](deployment-azure.md).

## Configuration reference

Configuration is `appsettings.json` → `appsettings.{Environment}.json` → environment variables (`Section__Key`). Environments:
`Development`, `Testing` (integration tests), `Staging`, `Production`. Secrets **never** live in source control.

| Key | Default | Notes |
| --- | --- | --- |
| `ConnectionStrings:Default` | — | SQL Server (required) |
| `ConnectionStrings:Redis` | empty → in-memory cache | |
| `Jwt:SigningKey` | — | ≥ 32 chars, **required**; startup fails on the development key outside Development/Testing |
| `Jwt:Issuer` / `Jwt:Audience` | `https://api.fundflow.com` / `fundflow-web` | |
| `Auth:*` | see `AuthOptions` | Token lifetimes, lockout, `RequireConfirmedEmail` |
| `App:PublicBaseUrl` | `http://localhost:3000` | Used to build links in emails |
| `Email:*` | Mailpit | SMTP relay |
| `RabbitMq:*` / `Messaging:Transport` | `RabbitMq` | `InMemory` for tests |
| `Hangfire:Enabled` / `PrepareSchema` / `WorkerCount` | `true` / `false` / 4 | Dashboard is Development-only |
| `Database:AutoMigrate` / `SeedDemoData` | `false` / `false` | `AutoMigrate` is a Development convenience. `SeedDemoData` (with `DemoPassword`) also runs in `--migrate`, for demo deployments only |
| `Spa:Enabled` | `true` | Serves `wwwroot/index.html` and its assets (single-origin hosting) when the app was published into `wwwroot` |
| `Database:SuperAdminEmail` / `SuperAdminPassword` | unset | Bootstraps the first platform operator during `--migrate` |
| `Cors:AllowedOrigins` | `[]` | Explicit origins only; credentials are allowed for them |
| `Proxy:TrustForwardedHeaders` | `false` | Set `true` only behind your own proxy |
| `RateLimiting:*` | see `RateLimitingOptions` | |
| `Security:PasswordHashIterations` | 210000 | Lower only in tests |
| `Swagger:Enabled` | `false` (true in Development) | |
| `OpenTelemetry:OtlpEndpoint` / `OTEL_EXPORTER_OTLP_ENDPOINT` | unset | Enables trace & metric export |
