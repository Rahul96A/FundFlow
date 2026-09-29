# ADR-0004: Transactional outbox and in-transaction domain events

**Status:** accepted · **Phase:** 1

## Context

Business operations have side effects (send a verification email, tell other modules a user was created). Doing them inline couples
request latency to SMTP/broker availability; doing them after commit risks losing them if the process dies between commit and send;
doing them before commit risks sending for a transaction that rolls back. The spec also forbids sending email from request handlers.

## Decision

* Aggregates **raise domain events** (pure, framework-free records in `Domain`).
* `AppDbContext.SaveChangesAsync` **dispatches them inside the unit of work, before the commit**, through MediatR
  (`DomainEventNotification<T>`), looping until no new events appear.
* Handlers may add rows and **outbox messages** (via `IEmailQueue` / `IIntegrationEventPublisher`, backed by MassTransit's EF Core
  *bus outbox*) but must not do external I/O. Everything commits in **one transaction**.
* MassTransit's delivery service then publishes committed outbox rows to RabbitMQ; consumers use the **inbox** so a redelivery is not
  processed twice. Email payloads containing one-time links are encrypted with Data Protection before they are stored or sent.
* Hangfire is used for scheduled/recurring work (housekeeping now; recurring donations and reports later), not for transactional messaging.

## Consequences

* ➕ Atomic: no email or event for a rolled-back change, none lost for a committed one (at-least-once delivery, deduplicated).
* ➕ Request handlers never wait on SMTP or the broker; a broker outage delays delivery but cannot fail user actions.
* ➕ Handlers are trivially unit-testable; integration tests run the real outbox against an in-memory transport.
* ➖ Domain event handlers run in the caller's transaction, so slow or failing handlers affect the request: they must stay cheap and I/O-free (documented in [conventions.md](../conventions.md)).
* ➖ Delivery is asynchronous (typically < 2 s locally, `QueryDelay` = 1 s); tests wait for it explicitly.
