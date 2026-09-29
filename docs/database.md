# Database migrations and operations

FundFlow uses **EF Core code-first migrations**. The migrations live in `src/FundFlow.Infrastructure/Persistence/Migrations`
and are applied to a database in exactly one of two ways:

| Environment | How migrations are applied |
| --- | --- |
| Development (`docker compose`, `dotnet run`) | Automatically at startup when `Database:AutoMigrate=true` (set in `appsettings.Development.json` and the compose file), followed by reference-data sync and demo seeding |
| Staging / Production | **An explicit deployment step**: `dotnet FundFlow.Api.dll --migrate`. The application never migrates implicitly (`AutoMigrate` defaults to `false`), and starting the app against an un-migrated database simply fails its readiness check |

## Commands

The `dotnet-ef` tool is pinned in the repo (`dotnet-tools.json`): `dotnet tool restore` once.

```bash
# Create a migration after changing entities or configurations
dotnet dotnet-ef migrations add <Name> \
  --project src/FundFlow.Infrastructure --startup-project src/FundFlow.Api \
  --output-dir Persistence/Migrations --context AppDbContext

# Undo the last (unapplied!) migration
dotnet dotnet-ef migrations remove --project src/FundFlow.Infrastructure --startup-project src/FundFlow.Api

# Apply to a specific database (development or a scratch database)
FUNDFLOW_CONNECTION_STRING="Server=127.0.0.1,1433;Database=FundFlow;User Id=sa;Password=…;TrustServerCertificate=True" \
  dotnet dotnet-ef database update --project src/FundFlow.Infrastructure --startup-project src/FundFlow.Api

# Review exactly what will run in production: an idempotent SQL script from any version to latest
dotnet dotnet-ef migrations script --idempotent --output migrate.sql \
  --project src/FundFlow.Infrastructure --startup-project src/FundFlow.Api

# CI gate: fails if the model changed without a migration
dotnet dotnet-ef migrations has-pending-model-changes --project src/FundFlow.Infrastructure --startup-project src/FundFlow.Api
```

Design-time tooling uses `DesignTimeDbContextFactory`, so generating a migration never needs a database or the full host.
The integration suite also asserts `Database.HasPendingModelChanges() == false` against a real server.

## The production migration step

```bash
# in a deployment job / Kubernetes Job, with a *deployment* identity that owns the schema
dotnet FundFlow.Api.dll --migrate
```

`--migrate` runs, in order, then exits without serving traffic:

1. **Schema migrations** (`Database.MigrateAsync`), inside EF's per-migration transactions.
2. **Reference-data sync**: upserts the permission catalogue, the platform `SUPER_ADMIN` role, and brings every organization's *system*
   roles in line with the code templates (batches of 200 tenants). It is additive and idempotent; custom roles are never touched.
3. **Hangfire schema** (when `Hangfire:PrepareSchema=true` for this run).
4. **Optional first platform operator**, only if `Database__SuperAdminEmail` and `Database__SuperAdminPassword` are supplied and no such
   account exists. Never modifies an existing account.

Run it *before* rolling out the new application version, and keep every migration **backward compatible with the previous
application version** (expand → deploy → contract) so a rolling deployment or a rollback never sees a schema it cannot use.

### Rules for writing migrations

* **Additive first.** Add nullable columns / new tables; backfill; only later make them required or drop the old ones in a *following* release.
* Never rename by drop+add: use `RenameColumn/RenameTable` (data-preserving) and review the generated code.
* Large tables (donations, audit logs): create indexes `ONLINE = ON` (Enterprise) or schedule off-peak; avoid blocking `ALTER COLUMN`.
* Data migrations belong in the migration (`migrationBuilder.Sql`) and must be idempotent and set-based.
* The generated snapshot and designer files are committed; never edit them by hand.
* Migrations are excluded from analysers and style rules (`.editorconfig`).

## Permissions the application needs

| Identity | Needs |
| --- | --- |
| Deployment (`--migrate`) | `db_owner` on the FundFlow database (DDL, `CREATE SCHEMA`) |
| Runtime API | `db_datareader`, `db_datawriter`. **No** DDL. Set `Hangfire__PrepareSchema=false` (default) so it never tries to create objects |

## Backup and restore (operational guidance)

* Use **FULL** recovery in production with frequent log backups (development uses the default simple behaviour).
* The audit log is the compliance record: include it in retention policies; do not truncate.
* Restoring to a point in time is safe: everything derived (Redis, tenant/permission caches, deny-list) rebuilds itself; restored
  outbox rows are re-delivered and consumers deduplicate through the inbox.

## Seed data

`Database:SeedDemoData=true` (Development only) creates:

| Tenant | Users |
| --- | --- |
| **Hope Foundation** (`hope-foundation`) | `admin@`, `fundraising@`, `finance@hopefoundation.test` (Organization admin, Fundraising manager, Finance manager) |
| **Riverside Animal Rescue** (`riverside-rescue`) | `admin@riverside.test` — a second tenant for trying isolation by hand |
| Platform | `superadmin@fundflow.test` |

The shared demo password is the `Database:DemoPassword` value in `src/FundFlow.Api/appsettings.Development.json`. Seeding is
idempotent (skips existing slugs). Later phases extend the seed to the full demo dataset (50 donors, 5 campaigns, 100 donations,
3 events, 20 auction items, 10 sponsors, 25 volunteers).
