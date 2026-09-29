-- FundFlow database bootstrap. Idempotent: safe to run on every `docker compose up`.
-- Run by the `sqlserver-init` one-shot service:  sqlcmd -v APP_PASSWORD=<password> -i init.sql
--
-- Schema objects (tables, indexes) are NOT created here; EF Core migrations own them.
-- This script owns what migrations should not: database-level settings and the login the API runs as.

SET NOCOUNT ON;
GO

IF DB_ID(N'FundFlow') IS NULL
BEGIN
    -- Case-insensitive, accent-sensitive: email/slug lookups behave the way users expect.
    CREATE DATABASE [FundFlow] COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Created database FundFlow';
END
GO

-- Row versioning instead of shared locks: readers never block writers and vice versa. Important for a workload that
-- mixes long-running report queries with high-rate writes (donations, bids). Also what makes optimistic concurrency
-- (rowversion) checks cheap.
ALTER DATABASE [FundFlow] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;
ALTER DATABASE [FundFlow] SET ALLOW_SNAPSHOT_ISOLATION ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'fundflow_app')
BEGIN
    -- Development: CHECK_POLICY off so the password in .env need not satisfy a Windows password policy.
    EXEC (N'CREATE LOGIN [fundflow_app] WITH PASSWORD = N''$(APP_PASSWORD)'', CHECK_POLICY = OFF');
    PRINT 'Created login fundflow_app';
END
ELSE
BEGIN
    EXEC (N'ALTER LOGIN [fundflow_app] WITH PASSWORD = N''$(APP_PASSWORD)''');
END
GO

USE [FundFlow];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'fundflow_app')
BEGIN
    CREATE USER [fundflow_app] FOR LOGIN [fundflow_app] WITH DEFAULT_SCHEMA = [dbo];
END
GO

-- db_owner is what lets the API apply EF migrations on startup in Development. In staging/production run
-- `FundFlow.Api --migrate` under a separate deployment identity and give the runtime login only:
--     ALTER ROLE db_datareader ADD MEMBER [fundflow_app];
--     ALTER ROLE db_datawriter ADD MEMBER [fundflow_app];
ALTER ROLE db_owner ADD MEMBER [fundflow_app];
GO
