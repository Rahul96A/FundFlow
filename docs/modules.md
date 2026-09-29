# Module boundaries

A **module** is a bounded slice of the business with its own entities, use cases, database schema and (where it has
one) UI feature folder. FundFlow ships as one deployable, so modules are separated by **convention enforced in review
and tests**, not by assembly boundaries. The rules below keep the boundaries real.

## Rules

1. **Own your data.** Only a module's own code reads or writes its tables. Other modules ask through an application
   service/query, or react to an integration event. No cross-module joins in application code.
2. **Depend on contracts, not internals.** A module may reference another module's *Contracts* (DTOs, integration events)
   and its published application abstractions, never its entities or `DbSet`s.
3. **Talk asynchronously across modules where you can.** Publish an integration event through the outbox rather than
   calling another module in-process. (`OrganizationRegistered`, `UserCreated`, … already work this way.)
4. **One schema per module.** `identity`, `organizations`, `audit`, `messaging`, `security`, `hangfire`.
   Extracting a module into a service later means moving a schema, not untangling joins.
5. **Tenant scoping is not optional.** Every tenant-owned entity implements `ITenantEntity`; the DbContext applies the
   filter by convention. See [multi-tenancy.md](multi-tenancy.md).

## Modules and what they own

| Module | Phase | Owns (entities) | Schema | Publishes | Depends on |
| --- | --- | --- | --- | --- | --- |
| **Identity** | 1 ✅ | User, Role, Permission, UserRole, RolePermission, UserSession, UserToken | `identity` | `UserCreated` | Organizations (tenant id only) |
| **Organizations** | 1 ✅ | Organization, OrganizationSettings | `organizations` | `OrganizationRegistered`, `OrganizationSuspended` | — |
| **Audit** | 1 ✅ | AuditLog | `audit` | — | — (written to by every module) |
| **Notifications** (system email) | 1 ✅ | — (outbox messages) | `messaging` | — | Identity events |
| **Donors** | 2 | Donor, DonorAddress, DonorContact, DonorTag, DonorNote, DonorInteraction, DonorRelationship | `donors` | `DonorCreated` | Organizations |
| **Campaigns** | 2 | Campaign, CampaignGoal, CampaignPage, CampaignMessage, CampaignMetric | `campaigns` | `CampaignPublished`, `CampaignCompleted` | Organizations, Payments (config check) |
| **Donations** | 2 | Donation, DonationLine, DonationAllocation, DonationRefund, DonationReceipt, RecurringDonation | `donations` | `DonationCreated`, `DonationSucceeded`, `DonationFailed`, `DonationRefunded` | Donors, Campaigns, Payments |
| **Events** | 3 | Event, EventTicket, EventRegistration, EventTable, EventSeat, EventCheckIn, EventSponsor | `events` | `EventRegistrationCreated`, `EventCheckInCompleted` | Donors, Payments |
| **Auctions** | 4 | Auction, AuctionItem, AuctionCategory, AuctionBid, AuctionWinner, AuctionPayment | `auctions` | `AuctionBidPlaced`, `AuctionClosed`, `AuctionWinnerSelected` | Donors, Events, Payments |
| **Sponsors** | 5 | Sponsor, SponsorPackage, SponsorBenefit | `sponsors` | `SponsorCreated` | Events |
| **Volunteers** | 5 | Volunteer, VolunteerAssignment, VolunteerShift | `volunteers` | — | Events |
| **Communications** | 5 | EmailCampaign, EmailTemplate, EmailRecipient, Notification | `communications` | — | Donors (segments) |
| **Reporting** | 6 | Report, ReportDefinition, ReportExecution | `reporting` | — | Read models of other modules |
| **Payments** | 7 | PaymentTransaction, PaymentMethod, PaymentProvider, PaymentWebhook | `payments` | `PaymentSucceeded`, `PaymentFailed` | — |

## Where a module lives in the code

For a module `Foo`:

```
src/FundFlow.Domain/Foo/                Entities, value objects, domain events, domain services
src/FundFlow.Contracts/Foo/             Request/response DTOs, integration events
src/FundFlow.Application/Foo/           Commands, queries, validators, event handlers, module abstractions
src/FundFlow.Infrastructure/Persistence/Configurations/   EF configuration for Foo's entities (schema "foo")
src/FundFlow.Api/Controllers/           FooController (thin)
frontend/src/features/foo/              api/ components/ hooks/ pages/ schemas/ types/
tests/…                                 Unit tests beside the layer; integration tests in the API test project
```

## Adding a module: checklist

1. Entities in `Domain/Foo` (implement `ITenantEntity` for tenant-owned data; raise domain events for facts others care about).
2. Register the schema name in `Persistence/Schemas.cs` and add `IEntityTypeConfiguration<T>` classes; `dotnet ef migrations add`.
3. Commands/queries + validators in `Application/Foo` (one vertical slice per file group).
4. Permissions in `Domain/Identity/Permissions.cs` and role templates in `SystemRoles.cs`; the next `--migrate` syncs them.
5. Thin controller with `[HasPermission]` on every action; contracts in `Contracts/Foo`.
6. Audit every sensitive mutation with `IAuditLogger`.
7. Tests: domain rules, validators, **a tenant-isolation test**, an authorization test, happy-path and failure-path API tests.
8. Frontend feature folder, route (with `handle.crumb`), navigation entry (flip `soon: true` off).
