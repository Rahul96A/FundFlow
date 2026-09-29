# ADR-0005: Permission-based authorization

**Status:** accepted · **Phase:** 1

## Context

The product has 11 named roles, and customers will want to tailor what people can do. Checking role names in endpoints hard-codes today's
org chart into the API and makes customization impossible.

## Decision

* A global **permission catalogue** (`Module.Action`, e.g. `Donor.Read`) is the unit of authorization. Endpoints declare
  `[HasPermission(Permissions.X.Y)]`; nothing checks role names.
* **Roles are per-tenant bundles of permissions.** System roles are created from code templates and are read-only; organizations
  create **custom roles** for anything else. Deployments re-sync system roles with the templates.
* Effective access is resolved from the database, cached briefly in Redis, and **invalidated when roles change**.
* **Escalation rules:** you can grant only permissions you hold (administrators excepted); only administrators may change administrators;
  an organization keeps at least one active administrator; tenant roles can never hold `Platform.Manage`.
* The fallback policy requires authentication, so new endpoints are private unless they explicitly opt out.

## Consequences

* ➕ Customization without code changes; least privilege by default; uniform enforcement and OpenAPI documentation of required permissions.
* ➕ Permissions can be added per module without touching existing roles beyond the template sync.
* ➖ An extra lookup per request (cached). Permissions live outside the JWT deliberately, trading a cache read for instant revocation.
* ➖ The catalogue is a compatibility surface: renaming a permission is a data migration.
