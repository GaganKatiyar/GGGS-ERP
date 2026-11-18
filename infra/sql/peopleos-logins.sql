-- SQL logins for the HR database (peopleos). Safe to run again: it only adds what is missing and
-- never changes an existing login's password.
--
--   gggs_admin  owner of the database: migrations, DevSeed, support queries. Member of rls_bypass,
--               so it sees every organisation's rows. Never used by the running apps.
--   gggs_app    the HR API and worker. Read/write only, NOT an owner and NOT in rls_bypass, so the
--               row-level security policy keeps each organisation's rows to itself.
--
-- The HR app's access to the platform database (NaBPlatform) is a third login, gggs_tenant, created by
-- scripts/nabplatform-logins.sql in the GGGS-ERP-PLATFORM repository.
--
-- Run as a sysadmin, for example:
--   sqlcmd -S localhost -E -I -i infra/sql/peopleos-logins.sql -v Database=peopleos AdminPassword="..." AppPassword="..."

SET NOCOUNT ON;
USE [master];

IF SUSER_ID(N'gggs_admin') IS NULL
    CREATE LOGIN [gggs_admin] WITH PASSWORD = N'$(AdminPassword)', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;

IF SUSER_ID(N'gggs_app') IS NULL
    CREATE LOGIN [gggs_app] WITH PASSWORD = N'$(AppPassword)', DEFAULT_DATABASE = [$(Database)], CHECK_POLICY = ON, CHECK_EXPIRATION = OFF;
GO

USE [$(Database)];

-- gggs_admin: owner, and allowed past row-level security.
IF DATABASE_PRINCIPAL_ID(N'gggs_admin') IS NULL CREATE USER [gggs_admin] FOR LOGIN [gggs_admin];
ALTER ROLE [db_owner] ADD MEMBER [gggs_admin];
IF DATABASE_PRINCIPAL_ID(N'rls_bypass') IS NULL CREATE ROLE [rls_bypass];
ALTER ROLE [rls_bypass] ADD MEMBER [gggs_admin];

-- gggs_app: read and write rows, nothing else.
IF DATABASE_PRINCIPAL_ID(N'gggs_app') IS NULL CREATE USER [gggs_app] FOR LOGIN [gggs_app];
ALTER ROLE [db_datareader] ADD MEMBER [gggs_app];
ALTER ROLE [db_datawriter] ADD MEMBER [gggs_app];

-- Undo anything that would switch row-level security off for the app.
IF IS_ROLEMEMBER(N'rls_bypass', N'gggs_app') = 1 ALTER ROLE [rls_bypass] DROP MEMBER [gggs_app];
IF IS_ROLEMEMBER(N'db_owner', N'gggs_app') = 1 ALTER ROLE [db_owner] DROP MEMBER [gggs_app];
GO

SELECT dp.name AS [user], r.name AS [role]
FROM sys.database_role_members m
JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
JOIN sys.database_principals dp ON dp.principal_id = m.member_principal_id
WHERE dp.name IN (N'gggs_admin', N'gggs_app')
ORDER BY dp.name, r.name;
GO
