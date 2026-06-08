-- ─────────────────────────────────────────────────────────────────────────────
--  Optional SQL Server bootstrap script.
--
--  This file is NOT auto-run by compose. EF Core migrations are the source
--  of truth for schema, and running this in parallel with `dotnet ef
--  database update` races. Use it only when you want a stand-alone DB
--  without the EF tooling (e.g., a quick `SELECT *` against a fresh
--  instance to confirm the server is reachable).
--
--  How to invoke (after `docker compose up -d`):
--     docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd \
--       -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C -i /init/init.sql
--
--  The compose file mounts this directory at /init read-only, so the
--  script is reachable inside the container without rebuilding.
-- ─────────────────────────────────────────────────────────────────────────────

IF DB_ID('RetailOms') IS NULL
BEGIN
    PRINT 'Creating database RetailOms';
    CREATE DATABASE RetailOms;
END
ELSE
BEGIN
    PRINT 'Database RetailOms already exists; no-op.';
END
GO

USE RetailOms;
GO

PRINT 'Connected to database: ' + DB_NAME();
PRINT 'SQL Server version: ' + @@VERSION;
GO
