# API architecture

* **Style:** REST over HTTPS, JSON (camelCase, enums as strings, ISO-8601 UTC timestamps).
* **Versioning:** URL segment, `/api/v1/…` (`Asp.Versioning`). Every response carries `api-supported-versions`. A breaking change
  ships as `/api/v2/…` while `v1` stays available and is marked deprecated in OpenAPI.
* **Documentation:** OpenAPI 3 at `/swagger/v1/swagger.json`, Swagger UI at `/swagger` (Development, or `Swagger:Enabled=true`).
  Each operation lists its required permission and the standard problem responses.
* **Controllers are thin:** bind → `IMediator.Send` → shape the response. All logic lives in handlers and the domain.
* **Request/response DTOs live in `FundFlow.Contracts`.** They are the public, versioned surface; commands in `Application` are
  a separate, internal shape mapped in the controller (explicit mapping, no mapper library).
* **Read queries are bound straight from the query string** (`[FromQuery] ListUsersQuery`); commands are mapped from the body.

## Conventions

### Pagination, sorting, filtering

```
GET /api/v1/users?page=1&pageSize=25&sortBy=email&sortDirection=asc&search=ada&status=Locked&roleId=…
```

```json
{ "items": [], "page": 1, "pageSize": 25, "totalCount": 1000, "totalPages": 40 }
```

* `pageSize` is clamped to 1–100: **no endpoint returns an unbounded collection**.
* `sortBy` is looked up in a per-endpoint **whitelist** (`SortMap<T>`); unknown values fall back to the default order and never reach
  the query. A stable tie-breaker on `Id` keeps pages from overlapping or skipping rows.
* `search` matches with `LIKE` after escaping `%`, `_`, `[`, so user input is always literal. Filters compose (AND).
* Large datasets (donations, donors) will additionally offer **keyset pagination** (`?after=<cursor>`) where offset paging degrades.

### Errors

Every error is `application/problem+json` (RFC 7807) with a `traceId` and `correlationId`:

```json
{
  "type": "https://api.fundflow.com/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "errors": { "email": ["A valid email address is required."] },
  "instance": "/api/v1/auth/register-organization",
  "traceId": "00-…", "correlationId": "…"
}
```

| Situation | Status | `type` suffix | `code` (examples) |
| --- | --- | --- | --- |
| Validation / malformed body / bad query | 400 | `validation` | — (`errors` map), `malformed_request` |
| Not authenticated / expired token | 401 | `unauthorized` | `invalid_credentials`, `email_not_verified`, `account_locked`, `invalid_refresh_token` |
| Authenticated but not allowed | 403 | `forbidden` | `privilege_escalation`, `tenant_mismatch`, `organization_suspended`, `csrf_header_missing` |
| Not found (or belongs to another tenant) | 404 | `not-found` | `not_found` |
| Conflict / concurrent edit / duplicate | 409 | `conflict` | `email_taken`, `slug_taken`, `role_name_taken`, `role_in_use`, `last_administrator`, `concurrency_conflict` |
| Business rule violated | 422 | `business-rule` | `role.platform_permission_forbidden`, … (domain rule codes) |
| Rate limited | 429 | `rate-limited` | `rate_limited` (+ `Retry-After`) |
| Unexpected | 500 | `internal` | `internal_error` — generic message, **no stack trace or internals**; quote the `traceId` |

`code` is the stable, machine-readable reason clients switch on; `detail` is human-readable and safe to show.

### Headers

| Header | Direction | Purpose |
| --- | --- | --- |
| `Authorization: Bearer <jwt>` | request | Access token |
| `X-FundFlow-Client: web` | request | Required on the cookie endpoints (`/auth/refresh`, `/auth/logout`) — CSRF defence |
| `X-Correlation-Id` | both | Optional inbound (validated: `[A-Za-z0-9._-]{8,64}`); always echoed; attached to every log line and error body |
| `Retry-After` | response | On 429 |
| `Cache-Control: no-store` | response | On every `/api/*` response (private, per-user data) |
| `Content-Security-Policy`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-*` | response | Hardening (see `SecurityHeadersMiddleware`) |

### Concurrency and idempotency

* Mutable aggregates expose a `rowversion`; a stale write is a **409 `concurrency_conflict`**.
* Password-reset/verification/invitation links are single-use.
* Payment webhooks (Phase 7) are idempotent by provider event id; creating donations will accept an `Idempotency-Key`.

## Endpoints (Phase 1)

Permission column: **A** = any signed-in user of the tenant, **Anon** = anonymous, otherwise the required permission.

### Authentication (`/api/v1/auth`)

| Method & path | Auth | Description |
| --- | --- | --- |
| `POST /register-organization` | Anon (rate-limited) | Create a tenant and its first administrator → `201` |
| `POST /login` | Anon (rate-limited) | Credentials → access token; sets refresh cookie |
| `POST /refresh` | Anon + cookie + `X-FundFlow-Client` | Rotate refresh token → new access token |
| `POST /logout` | Anon + cookie + `X-FundFlow-Client` | End the session; clear the cookie → `204` |
| `POST /forgot-password` | Anon | Email a reset link → always `202` |
| `POST /reset-password` | Anon | Choose a new password with the link token; signs out everywhere |
| `POST /verify-email` | Anon | Confirm an address |
| `POST /resend-verification` | Anon | Re-send the link → always `202` |
| `POST /accept-invitation` | Anon | Set a password from an invitation |
| `GET /me` · `PUT /me` | A | Profile, roles, **effective permissions**, organization · update own name/phone |
| `POST /change-password` | A | Requires the current password; signs out other devices |
| `GET /sessions` · `DELETE /sessions/{id}` | A | My devices · sign one out |

### Users (`/api/v1/users`)

| Method & path | Permission | Description |
| --- | --- | --- |
| `GET /` | `User.Read` | Paged list; `search`, `status`, `roleId`, `sortBy=name\|email\|lastLoginAt\|createdAt` |
| `GET /{id}` | `User.Read` | Detail with roles |
| `POST /` | `User.Manage` | Invite by email with roles → `201` |
| `PUT /{id}` | `User.Manage` | Update name/phone |
| `PUT /{id}/roles` | `User.Manage` | Replace roles (escalation & last-admin rules apply) |
| `POST /{id}/deactivate` · `/reactivate` · `/unlock` · `/resend-invitation` | `User.Manage` | Lifecycle actions |

### Roles and permissions

| Method & path | Permission | Description |
| --- | --- | --- |
| `GET /roles` · `GET /roles/{id}` | `Role.Read` | List with permission/user counts · detail with permission names |
| `POST /roles` · `PUT /roles/{id}` · `DELETE /roles/{id}` | `Role.Manage` | Custom roles only; system roles are immutable |
| `GET /permissions` | `Role.Read` | The catalogue, grouped by module |

### Organization

| Method & path | Permission | Description |
| --- | --- | --- |
| `GET /organization` | A | Profile + settings |
| `PUT /organization` | `Organization.Update` | Profile & address |
| `PUT /organization/settings` | `Organization.Update` | Time zone, currency, locale, fiscal year, branding |
| `GET /organization/overview` | A | Dashboard data; sections omitted when the caller lacks the permission |
| `GET /audit-logs` | `Audit.Read` | Paged; `action` (exact or `Module.` prefix), `entityType`, `userId`, `from`, `to`, `search` |
| `GET /public/organizations/{tenantSlug}` | Anon | Public-safe branding and currency for giving pages |

### Platform operators (`/api/v1/platform/organizations`)

| Method & path | Permission | Description |
| --- | --- | --- |
| `GET /` | `Platform.Manage` | Registry: search, `status`, sort |
| `POST /{id}/suspend` · `POST /{id}/activate` | `Platform.Manage` | Lock/unlock a tenant (reason recorded) |

### Health (anonymous)

| Path | Meaning |
| --- | --- |
| `/health/live` | Process is up (no dependency checks) — liveness probe |
| `/health/ready` | Database, cache and message bus reachable — readiness probe |
| `/health` | All checks; status only, never exception text or connection details |

## Webhooks (Phase 7)

`POST /api/v1/payments/webhooks/{provider}` is anonymous by necessity and protected instead by provider **signature
verification**, payload size limits, and **idempotent processing** keyed on the provider's event id (a replay is a no-op that
returns `200`). Failures are retried by Hangfire with back-off; the raw event is stored for replay.

## Rate limits

| Policy | Applies to | Default |
| --- | --- | --- |
| `auth` | login, refresh | 20 / minute / client IP |
| `sensitive` | register, forgot/reset password, verify/resend, accept invitation, change password | 10 / minute / client IP |
| global | everything else | 600 / minute per user (per IP when anonymous) |

Behind a reverse proxy set `Proxy:TrustForwardedHeaders=true` (only when the API is reachable exclusively through it) so limits and
audit records use the real client address rather than the proxy's.
