# ADR-0003: JWT access tokens with rotating cookie refresh tokens

**Status:** accepted · **Phase:** 1

## Context

The SPA needs sessions that are secure against XSS and CSRF, revocable, and cheap to authorize on every request. Options: server-side
sessions (cookie only), long-lived JWTs in `localStorage`, or short-lived JWT + refresh token.

## Decision

* **Access token:** 15-minute HS256 JWT, kept **in memory** by the SPA and sent as a bearer token. Claims: `sub`, `sid` (session),
  `tenant_id`, `scope`, `email`, `name`, `role`. *Permissions are not in the token* (they are resolved server-side and cached), so
  role changes take effect immediately and tokens stay small.
* **Refresh token:** opaque 256-bit random value in an **HttpOnly, SameSite=Strict** cookie scoped to `/api/v1/auth`. Only its SHA-256
  hash is stored. It **rotates on every use**; replay of a rotated token (outside a 10-second concurrency grace) revokes the whole session.
  Sliding 14-day and absolute 30-day expiry.
* **Revocation:** revoked session ids are written to a Redis deny-list for the access-token lifetime and checked on every request, so
  logout / device sign-out / password change / deactivation are immediate.
* **CSRF:** cookie endpoints require a custom header plus an `Origin` check on top of `SameSite=Strict`.
* **Same origin:** the SPA is served with `/api` proxied on the same origin, keeping the cookie first-party.

## Consequences

* ➕ XSS cannot read the long-lived credential; a leaked access token dies in 15 minutes or on revocation.
* ➕ Stateless authorization on the hot path (signature + Redis lookup), with real revocation.
* ➕ Sessions double as the "where am I signed in" list.
* ➖ More moving parts than a plain session cookie. Mitigated by single-flight refresh in the client, Web Lock across tabs, and tests for the races.
* ➖ HS256 uses a shared secret. Fine for a single issuer/verifier; moving to RS256/ES256 with a JWKS endpoint is isolated to `JwtTokenService` and the bearer options if third parties must verify tokens.
* ➖ Redis outage ⇒ deny-list checks fail open for ≤ one token lifetime (logged). Chosen over failing every request.
