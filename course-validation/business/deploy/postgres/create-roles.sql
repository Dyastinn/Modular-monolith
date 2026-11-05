-- One-time server setup, run as a PostgreSQL superuser:
--   psql -U postgres -v db=erp -v owner_password='...' -v app_password='...' -f create-roles.sql
--
-- erp_owner owns the database and runs migrations   → ConnectionStrings:ErpDbMigrations
-- erp_app   is what the web app connects as: rows only, no DDL → ConnectionStrings:ErpDb
-- If the app is ever compromised, it cannot drop or alter a table.

CREATE ROLE erp_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE erp_app   LOGIN PASSWORD :'app_password';

CREATE DATABASE :"db" OWNER erp_owner;
\connect :db

REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO erp_owner, erp_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Everything erp_owner creates from now on (i.e. every migration) is usable, but not changeable, by erp_app.
ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner GRANT USAGE ON SCHEMAS TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO erp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE erp_owner GRANT EXECUTE ON FUNCTIONS TO erp_app;
