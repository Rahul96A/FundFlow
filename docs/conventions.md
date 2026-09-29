# Coding conventions

These are the rules the codebase already follows. When in doubt, imitate the nearest existing file, and prefer the
simplest thing that keeps the boundaries in [architecture.md](architecture.md) intact.

## Principles

* **SOLID, DRY, KISS: in that order of importance for *this* codebase: KISS first.** No abstraction until a second use exists or a
  boundary demands it. Do not add layers "just in case".
* **Domain-driven where it pays.** Business rules live in the domain (entities/value objects/domain services) and are unit-testable
  without a database. Handlers orchestrate; controllers translate HTTP; components render.
* **Fail closed and fail loud.** Missing tenant scope returns nothing; unknown permission denies; invalid configuration stops startup.

## Backend (C# / .NET)

### Structure

* One folder per **module** inside each layer (`Identity`, `Organizations`, …). Vertical slices: a command, its validator and its
  handler live together in one file (`Application/Identity/Users/UserCommands.cs`).
* Controllers are ≤ a few lines per action: map request → command/query → return. **No business logic in controllers.**
* Domain entities have private setters and factory/behaviour methods; they enforce their own invariants and raise domain events.
  Nothing outside the domain assigns audit columns or `TenantId`.
* Domain events are handled **inside the unit of work** and must not perform I/O; side effects go through the outbox
  (`IEmailQueue`, `IIntegrationEventPublisher`).
* Public API shapes live in `Contracts`. Commands are internal; map explicitly in the controller.

### No generic repositories

`IAppDbContext` exposes `DbSet<T>`; handlers write the query they need and project (`Select`) to a DTO with `AsNoTracking()`. A
generic `IRepository<T>` would hide EF's power (projection, filters, `ExecuteDelete`) behind a leaky lowest-common-denominator API and add
a layer to keep in sync. Extract a named query/service only when several handlers share non-trivial logic (`IdentityQueries`).

### Data access rules

* Every list is **paged and bounded** (`PagedQuery`, max 100). Never load a whole table; never return an unbounded collection.
* Read paths: `AsNoTracking()` + projection. Sort only by whitelisted columns (`SortMap<T>`); escape `LIKE` input (`EscapeLike`).
* Money is `decimal(18,2)`; timestamps are `DateTimeOffset` in UTC via the injected `TimeProvider` (never `DateTime.Now`).
* `IgnoreQueryFilters()` needs a comment-worthy reason; see [multi-tenancy.md](multi-tenancy.md).
* Prefer `ExecuteUpdate/ExecuteDelete` for bulk maintenance; keep transactions short.
* Concurrency: mutable aggregates carry `rowversion`; surface `DbUpdateConcurrencyException` as 409.

### Errors

Throw, don't return error objects, and let `GlobalExceptionHandler` map them:

| Throw | Meaning | HTTP |
| --- | --- | --- |
| `ValidationException` (FluentValidation, or `Failures.Field(...)`) | bad input | 400 |
| `UnauthorizedException(message, code)` | authentication failed | 401 |
| `ForbiddenException(message, code)` | not allowed | 403 |
| `NotFoundException(resource, key)` | absent (or another tenant's) | 404 |
| `ConflictException(message, code)` | duplicate / state conflict | 409 |
| `DomainException(code, message)` | business rule broken | 422 |

Codes are stable identifiers (`snake_case` from handlers, `module.rule` from the domain); messages are user-safe.

### Security rules

* **Never log** passwords, tokens, secrets, card data or request bodies. The MediatR logging behaviour logs the request *type* only.
* Audit snapshots are explicit anonymous objects/DTOs (never entities); `AuditLogger` additionally redacts secret-looking keys.
* Compare secrets in constant time; store only hashes of tokens; use `RandomNumberGenerator`.
* New endpoint ⇒ `[HasPermission]` (or a deliberate `[AllowAnonymous]` with rate limiting) ⇒ audit if sensitive ⇒ tenant-isolation test.

### Style

File-scoped namespaces, primary constructors for DI, `sealed` by default, `record` for messages/DTOs, nullable reference types on,
**warnings are errors**. No magic strings: permissions, roles, audit actions, claim names and schema names are constants. XML doc
comments on public API-facing types explain *why*. Comments elsewhere explain non-obvious intent, not what the code says.

### Packages

Central package management (`Directory.Packages.props`). MediatR, MassTransit and FluentAssertions are pinned to their last
Apache-2.0 major lines: see [ADR-0006](adr/0006-dependency-licensing.md) before upgrading.

## Frontend (React / TypeScript)

### Structure

* **Feature folders** (`features/<name>/{api,components,hooks,pages,schemas,types}`); shared building blocks in `components/` and `shared/`.
  Avoid a giant `components/` dump: if only one feature uses it, it lives in that feature.
* **Pages compose; hooks fetch; `api/` modules call HTTP; components render.** No `fetch/axios` calls in components, no business rules in JSX.
* Routes are declared once (`app/router/routes.tsx`) with lazy pages, and paths are constants (`shared/constants/paths.ts`).

### State

| Kind | Tool |
| --- | --- |
| Server state (lists, details) | TanStack Query; keys from a per-feature `xKeys` factory; mutations invalidate precisely |
| Session / UI preferences | Zustand (`authStore`, `uiStore`), tiny and selector-driven |
| Table state (page, sort, search, filters) | The **URL** (`useTableParams`): shareable, refresh-safe |
| Form state | React Hook Form + Zod schema per form (mirrors server rules; server stays the authority) |

### Rules

* TypeScript `strict`; `erasableSyntaxOnly` (no enums/namespaces/parameter properties: use unions and `as const`); `verbatimModuleSyntax`
  (`import type`). No `any` (lint error).
* The **access token lives in memory only**. Never put credentials or tokens in `localStorage`, URLs or logs.
* Never `dangerouslySetInnerHTML`; user text is rendered as text. Redirect targets are validated (`safeReturnPath`).
* Server errors: form-field errors are mapped onto fields (`applyFieldErrors`); everything else is a toast or an inline `ErrorState`. Mutations
  that render their own error set `meta: { silent: true }`.
* Every async surface has **loading, empty and error** states (`LoadingState`, `EmptyState`, `ErrorState`). Tables use `DataTable`.
* **Accessibility:** semantic landmarks, labelled controls, keyboard-operable everything, `aria-sort`/`aria-busy` on tables, focus rings kept,
  colour never the only signal (badges carry text), skip link, dialogs trap focus.
* Permission checks in the UI are for **usability only** (`usePermission`, route guards); the API enforces the real rule.

### Naming

Components `PascalCase.tsx`; hooks `useThing.ts`; API modules `thingApi.ts`; types `thing.types.ts`; schemas `thingSchemas.ts`; tests beside the
code as `*.test.ts(x)`.

## Tests

* **Test behaviour, not implementation.** Prefer integration tests through the real pipeline for anything involving auth, tenancy or the database.
* Every feature needs: domain-rule unit tests, validator tests, an authorization test, a **tenant-isolation test**, happy and failure paths.
* Integration tests build state through the public API (register → email → verify → login), so setup exercises real paths.
* Tests must be independent: create your own tenant with unique names; never reset shared data.
* Frontend: Vitest + Testing Library for components/hooks/utilities; Playwright for end-to-end flows against the running stack.

## Git and reviews

Small, focused commits; imperative subject lines; PRs describe *why*. CI must be green (build with warnings-as-errors, all tests, lint,
type-check, production build, dependency audit, image builds) before merge.
