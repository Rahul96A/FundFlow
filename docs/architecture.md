# FundFlow architecture

FundFlow is a multi-tenant SaaS platform for nonprofit fundraising: donor CRM, campaigns, donations, events, auctions,
sponsors, volunteers, communications and reporting. It is built as a **modular monolith**: one deployable API, one
database, strict internal module boundaries, designed so a module can be extracted later without a rewrite.

> Status: **Phase 1 is complete** (platform foundation: identity, tenants, security, base UI, infrastructure).
> See [roadmap.md](roadmap.md). Business modules (donors, campaigns, …) arrive in Phases 2–7 on top of this foundation.

## System context

```mermaid
flowchart LR
    subgraph Users
        A[Nonprofit staff<br/>browser SPA]
        D[Donors / guests<br/>public pages, Phase 2+]
        O[Platform operator<br/>SUPER_ADMIN]
    end

    subgraph FundFlow
        WEB[React SPA<br/>nginx]
        API[ASP.NET Core API<br/>modular monolith]
        SQL[(SQL Server<br/>schema per module)]
        REDIS[(Redis<br/>cache + session deny-list)]
        MQ[[RabbitMQ<br/>MassTransit]]
        HF[Hangfire<br/>scheduled jobs]
    end

    SMTP[SMTP relay<br/>Mailpit in dev]
    PAY[Payment provider<br/>Stripe, Phase 7]
    OTEL[OpenTelemetry collector<br/>optional]

    A --> WEB
    D --> WEB
    O --> WEB
    WEB -- "/api/*  (same origin)" --> API
    API --> SQL
    API --> REDIS
    API -- "transactional outbox" --> MQ
    MQ -- "consumers (same process)" --> API
    HF --> SQL
    API --> HF
    API --> SMTP
    API -.-> PAY
    API -.-> OTEL
```

The browser only ever talks to **its own origin**. nginx (Docker/production) or the Vite dev server proxies `/api` to
the API, so the HttpOnly refresh-token cookie is first-party and no CORS is needed for the SPA.

## Repository layout

```
FundFlow/
├─ src/
│  ├─ FundFlow.Domain/          Entities, value objects, domain events, business rules. No dependencies.
│  ├─ FundFlow.Contracts/       Public request/response DTOs and integration events. No dependencies.
│  ├─ FundFlow.Application/     Use cases (MediatR commands/queries), validators, abstractions (ports).
│  ├─ FundFlow.Infrastructure/  EF Core, Redis, MassTransit, Hangfire, SMTP, JWT: implements the ports.
│  └─ FundFlow.Api/             ASP.NET Core host: controllers, middleware, auth, Swagger, telemetry.
├─ tests/
│  ├─ FundFlow.Domain.Tests/            Pure unit tests of business rules.
│  ├─ FundFlow.Application.Tests/       Handlers' collaborators, validation, security services, tenant provider.
│  └─ FundFlow.Api.IntegrationTests/    Real API + real SQL Server (Testcontainers): tenant isolation, auth, authz…
├─ frontend/                    React + TypeScript SPA (feature-based)
├─ infra/                       SQL Server, Redis and RabbitMQ configuration
├─ docs/                        You are here
├─ docker-compose.yml           Full local stack
└─ .github/workflows/ci.yml
```

### Layering (Clean Architecture)

```mermaid
flowchart TB
    API[FundFlow.Api] --> APP[FundFlow.Application]
    API --> INF[FundFlow.Infrastructure]
    INF --> APP
    APP --> DOM[FundFlow.Domain]
    APP --> CON[FundFlow.Contracts]
    INF --> DOM
    INF --> CON
    API --> CON
```

Dependencies point inwards. `Domain` and `Contracts` have **no** package dependencies. `Application` depends on
`Microsoft.EntityFrameworkCore` (for `DbSet<T>` and `IQueryable` operators) but not on any database provider:
handlers query `IAppDbContext` directly and project to DTOs rather than going through per-entity repositories
(see [conventions.md](conventions.md#no-generic-repositories)).

### Modules

Modules are **folders/namespaces inside each layer** (`Identity`, `Organizations`, `Audit`, …), not separate assemblies.
Each module also owns a **SQL schema** (`identity`, `organizations`, `audit`, …). See [modules.md](modules.md) for the
boundary rules and the planned modules.

## Request lifecycle

```mermaid
sequenceDiagram
    autonumber
    participant B as Browser
    participant M as Middleware pipeline
    participant C as Controller
    participant P as MediatR pipeline
    participant H as Handler
    participant DB as AppDbContext
    participant SQL as SQL Server

    B->>M: GET /api/v1/users  (Bearer JWT)
    M->>M: Correlation ID · Serilog · exception handler · security headers
    M->>M: JWT validation + session deny-list check (Redis)
    M->>M: TenantMiddleware → ITenantScope.UseTenant(tid)
    M->>M: Authorization: [HasPermission("User.Read")] → cached permission lookup
    M->>C: ListUsers(query)
    C->>P: Send(ListUsersQuery)
    P->>P: Logging → FluentValidation
    P->>H: Handle
    H->>DB: db.Users.AsNoTracking().Where(...)
    DB->>DB: Global query filter: TenantId = current tenant
    DB->>SQL: SELECT ... WHERE TenantId = @tid AND ...
    SQL-->>H: rows
    H-->>C: PagedResponse<UserSummary>
    C-->>B: 200 JSON (Cache-Control: no-store)
```

Write operations add three steps between the handler and the response:

1. The handler mutates domain objects (which raise **domain events**) and stages an **audit record**.
2. `SaveChangesAsync` first dispatches domain events *inside the unit of work* (handlers may enqueue emails and
   integration events into the **transactional outbox**), then the save interceptor stamps audit columns and enforces
   the tenant boundary, then everything commits in one transaction.
3. After commit, MassTransit's delivery service publishes outbox messages to RabbitMQ; consumers (e.g. email sending)
   run asynchronously, deduplicated through the inbox.

## Asynchronous processing

| Concern | Mechanism | Guarantee |
| --- | --- | --- |
| System emails (verify, reset, invite) | Domain event → outbox message → RabbitMQ → consumer → SMTP | At-least-once delivery, deduplicated by MessageId; never sent if the business transaction rolls back |
| Integration events (`UserCreated`, `OrganizationRegistered`, …) | Domain event → outbox → RabbitMQ | Same |
| Housekeeping (expired sessions/tokens) | Hangfire recurring job | Idempotent (`DELETE … WHERE older than`); overlapping runs prevented |

Email bodies contain one-time links, so payloads are encrypted with ASP.NET Core Data Protection before they enter the
outbox table or the broker.

## Cross-cutting concerns

| Concern | Where | Notes |
| --- | --- | --- |
| Multi-tenancy | `AppDbContext` query filters, `EntitySaveInterceptor`, `TenantMiddleware` | [multi-tenancy.md](multi-tenancy.md) |
| Authentication | JWT access token + rotating refresh cookie | [authentication.md](authentication.md) |
| Authorization | Permission policies (`[HasPermission]`) | Roles are only bundles of permissions |
| Validation | FluentValidation in the MediatR pipeline | Domain also enforces its own invariants |
| Errors | `GlobalExceptionHandler` → RFC 7807 | [api.md](api.md#errors) |
| Audit | `IAuditLogger` staged in the same transaction | Append-only; secrets redacted |
| Observability | Serilog (JSON), OpenTelemetry, health checks | Correlation ID on every log line and response |
| Rate limiting | ASP.NET Core rate limiter | Per-IP on auth endpoints, per-user global bucket |

## Deployment view

```mermaid
flowchart LR
    LB[Ingress / load balancer<br/>TLS termination] --> WEB1[web x N<br/>nginx + SPA]
    LB --> API1[api x N]
    WEB1 -- /api --> API1
    API1 --> SQL[(SQL Server)]
    API1 --> R[(Redis)]
    API1 --> MQ[[RabbitMQ]]
    MIG[migration job<br/>FundFlow.Api --migrate] --> SQL
```

* The API is **stateless** (no in-process session state; caches and deny-lists live in Redis), so it scales horizontally.
* Hangfire and MassTransit consumers run inside API instances; `DisableConcurrentExecution` and the inbox make
  multiple instances safe. They can be split into a dedicated worker deployment without code changes.
* Database migrations are an **explicit deployment step** (`dotnet FundFlow.Api.dll --migrate`), never an implicit side
  effect of starting the app in production. See [database.md](database.md).

## Key decisions

Recorded as ADRs in [docs/adr](adr):

1. [Modular monolith](adr/0001-modular-monolith.md)
2. [Shared database, shared schema multi-tenancy](adr/0002-multi-tenancy.md)
3. [JWT access tokens with rotating cookie refresh tokens](adr/0003-authentication-tokens.md)
4. [Transactional outbox and in-transaction domain events](adr/0004-outbox-and-domain-events.md)
5. [Permission-based authorization](adr/0005-permission-authorization.md)
6. [Dependency licensing constraints](adr/0006-dependency-licensing.md)
