# ADR-0002: Shared database, shared schema multi-tenancy

**Status:** accepted · **Phase:** 1

## Context

FundFlow must host 10,000+ nonprofit organizations. A tenant's donors, donations and financial data must never leak to another tenant.
Options considered: database-per-tenant, schema-per-tenant, shared schema with a `TenantId` discriminator.

## Decision

**Shared database, shared schema, `TenantId` column** on every tenant-owned table, with isolation enforced in **layers** so no single
mistake exposes data:

1. tenant resolved from the validated JWT (never from a client-supplied header);
2. EF Core **global query filters applied by convention** to every tenant-owned entity type;
3. a **save interceptor** that rejects writes crossing a tenant boundary;
4. an **immutable tenant scope** per request;
5. foreign keys to `Organizations` and tenant-leading indexes;
6. automated **tenant-isolation integration tests** against a real SQL Server.

Users are unique platform-wide by email and belong to exactly one organization; platform operators (`SUPER_ADMIN`) have `TenantId = NULL`
and a separate *platform scope* that sees the organization registry but never tenant data.

## Consequences

* ➕ Cheap to run and operate at thousands of tenants; one migration updates everyone; simple cross-tenant analytics for the operator.
* ➕ Isolation is testable and fails closed (no scope ⇒ no rows).
* ➖ A noisy tenant can affect others: mitigated by rate limits, bounded queries, snapshot isolation and (later) per-tenant quotas.
* ➖ Per-tenant backup/restore and data residency are harder. If a customer requires it, that tenant can be moved to a dedicated database
  because the `TenantId` discriminator and the tenant directory already exist (a database-per-tenant *router* is the migration path).
* ➖ **Email is globally unique**, so one person cannot belong to two organizations with the same address. Multi-organization membership
  is a future migration (a `Membership` table plus tenant choice at sign-in); nothing downstream needs to change because tokens already
  carry a single `tenant_id`.

## Alternatives rejected

* *Database-per-tenant:* strongest isolation but 10,000 databases means per-tenant migrations, connection-pool fragmentation and operational cost that a small team cannot carry yet.
* *Schema-per-tenant:* migrations and EF model caching scale poorly with thousands of schemas.
