# FundFlow

Enterprise fundraising, donor management, events and auctions for nonprofits: a multi-tenant SaaS platform.

> **Status: Phase 1 complete**: platform foundation (architecture, Docker, database, identity, tenant management, security, base UI).
> Donors, campaigns, donations, events, auctions and the rest are Phases 2–7 ([roadmap](docs/roadmap.md)).

| | |
| --- | --- |
| Backend | .NET 10, ASP.NET Core, EF Core, SQL Server, Redis, MassTransit + RabbitMQ, Hangfire, Serilog, OpenTelemetry |
| Frontend | React 19, TypeScript (strict), Vite, MUI, TanStack Query, Zustand, React Hook Form + Zod, Recharts |
| Tests | xUnit + FluentAssertions + Moq + Testcontainers (real SQL Server); Vitest + Testing Library; Playwright |
| Architecture | Modular monolith, Clean Architecture, CQRS-style use cases (MediatR), transactional outbox |

## Quick start (Docker)

Prerequisites: Docker Desktop (or Docker Engine + Compose v2). Nothing else.

```bash
cp .env.example .env            # local development secrets; change them if the machine is shared
docker compose up -d --build    # SQL Server, Redis, RabbitMQ, Mailpit, API, web
```

| What | URL |
| --- | --- |
| Web app | <http://localhost:3000> |
| API + Swagger UI | <http://localhost:5080/swagger> |
| Mailpit (every email the system sends) | <http://localhost:8025> |
| RabbitMQ management | <http://localhost:15672> (user/password from `.env`) |
| Hangfire dashboard (Development only) | <http://localhost:5080/hangfire> |

The first start applies migrations and seeds two demo organizations. Sign in at <http://localhost:3000>:

| Account | Role |
| --- | --- |
| `admin@hopefoundation.test` | Organization admin, **Hope Foundation** |
| `fundraising@hopefoundation.test` | Fundraising manager, Hope Foundation |
| `finance@hopefoundation.test` | Finance manager, Hope Foundation |
| `admin@riverside.test` | Organization admin, **Riverside Animal Rescue** (a second tenant, to try isolation) |
| `superadmin@fundflow.test` | Platform operator (manages organizations; sees no tenant data) |

The demo password is `Database:DemoPassword` in [`src/FundFlow.Api/appsettings.Development.json`](src/FundFlow.Api/appsettings.Development.json).
You can also register a brand-new organization at `/register`; the verification email appears in Mailpit.

Stop with `docker compose down` (add `-v` to also delete the data volumes).

## Everyday development

Run the dependencies in Docker and the apps on your machine for fast feedback:

```bash
docker compose up -d sqlserver sqlserver-init redis rabbitmq mailpit

dotnet run --project src/FundFlow.Api                # http://localhost:5080  (migrates + seeds in Development)
cd frontend && npm install && npm run dev            # http://localhost:5173  (proxies /api to the API)
```

The Definition of Done for any change: it builds with **zero warnings**, all tests pass, and lint/type-check are clean.

```bash
dotnet build                                          # warnings are errors
dotnet test                                           # unit + integration (integration tests need Docker)
cd frontend && npm run lint && npm run typecheck && npm test -- --run && npm run build
```

More: **[docs/development.md](docs/development.md)**: environments, secrets, migrations, testing, troubleshooting.

## Documentation

| Topic | Document |
| --- | --- |
| Architecture, layers, request lifecycle, diagrams | [docs/architecture.md](docs/architecture.md) |
| Module boundaries and how to add a module | [docs/modules.md](docs/modules.md) |
| Database ERD (implemented + planned) | [docs/erd.md](docs/erd.md) |
| Multi-tenant design and its tests | [docs/multi-tenancy.md](docs/multi-tenancy.md) |
| Authentication & authorization | [docs/authentication.md](docs/authentication.md) |
| API conventions and endpoint reference | [docs/api.md](docs/api.md) |
| SQL Server / Redis / RabbitMQ / containers / configuration | [docs/infrastructure.md](docs/infrastructure.md) |
| Migrations and production database operations | [docs/database.md](docs/database.md) |
| Coding conventions (backend, frontend, tests) | [docs/conventions.md](docs/conventions.md) |
| Architecture decision records | [docs/adr](docs/adr) |
| Deploying to Azure's free tier (one command) | [docs/deployment-azure.md](docs/deployment-azure.md) |
| Roadmap and known limitations | [docs/roadmap.md](docs/roadmap.md) |

## Repository layout

```
src/            FundFlow.Domain · Contracts · Application · Infrastructure · Api
tests/          Domain.Tests · Application.Tests · Api.IntegrationTests
frontend/       React SPA (features/, components/, shared/, app/)
infra/          SQL Server, Redis, RabbitMQ configuration
docs/           Architecture and operations documentation
```

## Security

Report vulnerabilities privately to your security contact. Highlights: tenant isolation enforced in layers and tested; PBKDF2 password
hashing; short-lived JWT + rotating HttpOnly refresh cookie with theft detection; permission-based authorization with privilege-escalation
guards; append-only, secret-redacting audit log; rate limiting; strict security headers; encrypted email payloads; no secrets in source
control. Details in [docs/authentication.md](docs/authentication.md) and [docs/multi-tenancy.md](docs/multi-tenancy.md).

## License

Released under the [MIT License](LICENSE). Copyright (c) 2026 Rahul Anandpara.
