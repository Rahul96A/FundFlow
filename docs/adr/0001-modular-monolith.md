# ADR-0001: Modular monolith

**Status:** accepted · **Phase:** 1

## Context

FundFlow spans a wide domain (donors, campaigns, donations, events, auctions, sponsors, volunteers, communications, reporting,
payments) but starts with a small team and one product. Microservices would add distributed-systems cost (network failure modes,
distributed transactions, per-service deployment and observability) before the domain boundaries are proven. A single
"big ball of mud" monolith would make later extraction painful.

## Decision

Build **one deployable** with **strict internal module boundaries**:

* Five projects by layer (`Domain`, `Contracts`, `Application`, `Infrastructure`, `Api`), with dependencies pointing inwards.
* Within each layer, one folder/namespace per module; one **SQL schema** per module; no cross-module joins or entity sharing.
* Cross-module reactions use **integration events through a transactional outbox**, so extracting a module later means moving a
  schema and a consumer, not rewriting call sites.

Modules are *not* separate assemblies yet: the boundary is convention enforced in review and (later) architecture tests, which keeps
the build fast and refactoring cheap while the domain is still moving.

## Consequences

* ➕ One transaction for a use case; simple local development, debugging and deployment.
* ➕ Clear seams for extraction (schema, events, contracts).
* ➖ Boundaries can erode without discipline: mitigated by [modules.md](../modules.md), code review and planned architecture tests (Phase 8).
* ➖ All modules scale together; the API is stateless so it scales horizontally, and background workers can be split out by configuration.
