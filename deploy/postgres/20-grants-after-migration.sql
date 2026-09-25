-- ILM Portal: least-privilege grants. Run as ilm_migrator after every `Ilm.Web migrate`.
-- The application identity gets DML only (no DDL, no ownership). Audit and transition history are
-- INSERT/SELECT only; the append-only triggers in the migrations additionally block UPDATE/DELETE/TRUNCATE
-- for every role, including the owner.
GRANT USAGE ON SCHEMA ilm TO ilm_app;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA ilm TO ilm_app;
REVOKE UPDATE, DELETE, TRUNCATE ON ilm."AuditRecords", ilm."LeaverTransitions" FROM ilm_app;
REVOKE INSERT, UPDATE, DELETE, TRUNCATE ON ilm."__EFMigrationsHistory" FROM ilm_app;
GRANT SELECT ON ilm."AuditRecords", ilm."LeaverTransitions", ilm."AuditForwardingCheckpoints" TO ilm_auditor_ro;
