# Delivery roadmap

Each phase is delivered **completely** (frontend + API + domain + database + validation + authorization + audit + error handling +
tests + documentation) before the next begins.

| Phase | Scope | Status |
| --- | --- | --- |
| **1** | Architecture, repository, Docker, database, identity, tenant management, base UI, authentication | ✅ **Done** |
| **2** | Donors (CRM, segments), campaigns (lifecycle, public pages), donations (one-time, anonymous, tribute, refunds) | ⏳ Next |
| **3** | Events, registrations, ticketing, QR check-in, seating, waitlist | |
| **4** | Auctions, bidding with concurrency protection, winner processing | |
| **5** | Sponsors, volunteers, communications (templates, campaigns, notification centre) | |
| **6** | Reports, analytics, asynchronous CSV/Excel/PDF exports, executive dashboard | |
| **7** | Payments: Stripe provider behind `IPaymentProvider`, webhooks (idempotent), recurring donations | |
| **8** | Observability & security hardening, performance, full test pyramid, production CI/CD, Kubernetes manifests | |

## Phase 1: what was delivered

* **Platform:** modular-monolith solution (Domain / Contracts / Application / Infrastructure / Api), central package management,
  warnings-as-errors, `.editorconfig`, pinned tools.
* **Tenancy:** shared-schema isolation enforced on reads (global query filters by convention), writes (save interceptor), scope
  (immutable) and resolution (`TenantMiddleware`); tenant registration, suspension/reactivation, public tenant info.
* **Identity:** registration, email verification, login with lockout, JWT access + rotating cookie refresh tokens with theft
  detection, session listing/revocation, password reset/change, invitations, profile.
* **Authorization:** permission catalogue (36 permissions), 11 roles, custom roles, escalation guard, last-admin guard, immediate
  effect of changes.
* **Audit:** append-only, transactional, secret-redacting, filterable UI.
* **Async:** transactional outbox + RabbitMQ (MassTransit), encrypted email payloads, Hangfire housekeeping.
* **Ops:** Docker Compose (SQL Server, Redis, RabbitMQ, Mailpit, API, web), health checks, structured logs, OpenTelemetry, CI.
  One-command deployment to **Azure's free tier** (Bicep + script; the API serves the React app as a single origin), see
  [deployment-azure.md](deployment-azure.md).
* **UI:** app shell with collapsible/responsive navigation, design-system components, dark mode, auth flows, dashboard, organization
  settings, users, roles & permissions, audit log, account & security, platform registry.
* **Quality:** unit, integration (real SQL Server) and frontend tests; details in [development.md](development.md#testing).

## Known Phase-1 limitations (tracked)

* MFA and OIDC/SSO are designed for ([authentication.md](authentication.md#extension-points-deliberately-not-built-yet)) but not built.
* The Hangfire dashboard is enabled only in Development; production access will be fronted by SSO in Phase 8.
* Rate limiting is per API instance (in-memory); a distributed limiter (Redis) is a Phase 8 item before horizontal scale-out.
* The Azure free-tier deployment is deliberately single-instance (in-process cache and message transport, no Hangfire, no Always On)
  and the SQL free allowance covers roughly 35 sessions a month; it is a demo/pilot host, not a production topology
  ([what changes for production](deployment-azure.md#growing-out-of-the-free-tier)).
* The global search box, notification centre and end-to-end coverage beyond the onboarding, invitation, password-reset and
  role-visibility flows arrive with the phases that own their data.
