-- ILM Portal: PostgreSQL roles and database.
-- Run once as a PostgreSQL administrator with psql, supplying passwords as variables, for example:
--   psql -v migrator_password="$(read secret)" -v app_password="$(read secret)" -f 00-create-roles-and-database.sql
-- Prefer Kerberos/GSSAPI (pg_hba.conf "gss") for ilm_app on Windows so no password exists at all;
-- the password lines are then removed and the roles map to the gMSA principal.
-- Separation of duties:
--   ilm_migrator  owns the schema; used only by `Ilm.Web migrate` during change windows.
--   ilm_app       the runtime identity: DML only; INSERT/SELECT only on audit and transition history.
--   ilm_auditor_ro  read-only access to audit history for exports and SIEM reconciliation.
--   ilm_dba       platform administration; holds NO application lifecycle role.
CREATE ROLE ilm_migrator LOGIN PASSWORD :'migrator_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE ilm_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION;
CREATE ROLE ilm_auditor_ro NOLOGIN;
CREATE ROLE ilm_dba NOLOGIN;
CREATE DATABASE ilm OWNER ilm_migrator ENCODING 'UTF8';
\connect ilm
REVOKE ALL ON DATABASE ilm FROM PUBLIC;
GRANT CONNECT ON DATABASE ilm TO ilm_app, ilm_migrator;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA IF NOT EXISTS ilm AUTHORIZATION ilm_migrator;
REVOKE ALL ON SCHEMA ilm FROM PUBLIC;
GRANT USAGE ON SCHEMA ilm TO ilm_app, ilm_auditor_ro;
