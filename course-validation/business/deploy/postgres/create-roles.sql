-- One-time server setup, run as a PostgreSQL superuser:
--   psql -U postgres -v db=yasda -v owner_password='...' -v app_password='...' -f create-roles.sql
--
-- yasda_owner owns the database and runs migrations   → ConnectionStrings:YasdaDbMigrations
-- yasda_app   is what the web app connects as: rows only, no DDL → ConnectionStrings:YasdaDb
-- If the app is ever compromised, it cannot drop or alter a table.

CREATE ROLE yasda_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE yasda_app   LOGIN PASSWORD :'app_password';

CREATE DATABASE :"db" OWNER yasda_owner;
\connect :db

REVOKE ALL ON DATABASE :"db" FROM PUBLIC;
GRANT CONNECT ON DATABASE :"db" TO yasda_owner, yasda_app;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;

-- Everything yasda_owner creates from now on (i.e. every migration) is usable, but not changeable, by yasda_app.
ALTER DEFAULT PRIVILEGES FOR ROLE yasda_owner GRANT USAGE ON SCHEMAS TO yasda_app;
ALTER DEFAULT PRIVILEGES FOR ROLE yasda_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO yasda_app;
ALTER DEFAULT PRIVILEGES FOR ROLE yasda_owner GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO yasda_app;
ALTER DEFAULT PRIVILEGES FOR ROLE yasda_owner GRANT EXECUTE ON FUNCTIONS TO yasda_app;
