# ADR-0006: Dependency licensing constraints

**Status:** accepted · **Phase:** 1

## Context

Several popular .NET libraries changed licence terms after the versions FundFlow's specification was written against. Using the latest
major version of these would put a commercial licence obligation (or a licence-key runtime requirement) on a product that must be freely
deployable by nonprofits and self-hosters.

| Library | Free (Apache-2.0) line | Commercial from | Decision |
| --- | --- | --- | --- |
| **MediatR** | 12.x | 13.0 | Pin **12.5.0** |
| **MassTransit** | 8.x | 9.0 | Pin **8.5.x** (`MassTransit`, `.RabbitMQ`, `.EntityFrameworkCore`) |
| **FluentAssertions** (tests) | 7.x | 8.0 | Pin **7.2.x** |
| **AutoMapper** | — | 15.0 | **Not used.** Mapping is explicit (`Select` projections, small mapping methods) |

## Decision

Stay on the last Apache-2.0 major line of each, recorded in `Directory.Packages.props`. Do not bump these across a major version without
re-reading the licence and recording a new ADR. Security fixes within the pinned major line are taken normally (`dotnet list package --vulnerable`
runs in CI).

## Consequences

* ➕ No licence keys, no revenue thresholds, no surprise obligations for self-hosted deployments.
* ➖ New features of the newer majors are unavailable. The pinned lines are stable and sufficient; the seams (`IPublisher`, `IPublishEndpoint`,
  the outbox) are thin, so a future migration to a replacement (e.g. Wolverine, or a hand-rolled mediator) is contained.
* Also avoided by design: Mapster/AutoMapper reflection magic. Explicit projections are faster in EF and easier to review.
