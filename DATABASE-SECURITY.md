# Database security

The ILM database holds personal data (names, departments, account identifiers, leaver reasons) and **privileged topology**: which objects are protected, where the OUs are, and who can approve what. Protect it like a Tier 1 system. The application can't prevent a DBA from reading or altering rows, but it makes sure a DBA can't **silently** rewrite lifecycle history.

Production uses **PostgreSQL 16 or later**. SQLite is used only for isolated development and tests. Production startup refuses SQLite (`ConnectionSecurityValidator`).

## Controls

| Control | Implementation | Verified here |
|---|---|---|
| **Encryption at rest** | Use the platform's encryption: BitLocker on the Windows volume, or LUKS or dm-crypt on Linux, or the cloud provider's managed-disk encryption with customer-managed keys. The WAL and temporary tablespaces must sit on encrypted volumes too. | Operational (not testable here) |
| **TLS** for connections | The server has `ssl = on` and `ssl_min_protocol_version = TLSv1.2`. ILM refuses to start in production unless the connection string has `SSL Mode=VerifyFull` and no `Trust Server Certificate=true`. | `DatabaseSecurityTests.Production_requires_encrypted_postgresql_connection` |
| **Least-privilege application identity** | `ilm_app` has DML only, with no DDL, ownership or `CREATE`. It has only INSERT and SELECT on `AuditRecords` and `LeaverTransitions`, and no write on `__EFMigrationsHistory`. Production refuses `postgres` or `ilm_migrator` as the application user. | `PostgresPrivilegeTests` (run against PostgreSQL 16 in Docker) |
| Separate **migration identity** | `ilm_migrator` owns the schema. It's used only by `Ilm.Web migrate` during change windows, then `20-grants-after-migration.sql` re-applies the grants. | `PostgresPrivilegeTests` |
| Separate **DBA** role | `ilm_dba` (NOLOGIN) is granted to named DBA logins. It holds no ILM application role, and the application has no "DBA" role. | By design, documented |
| No shared administrator credentials | Each DBA has a named login mapped through `pg_ident` (see `deploy/postgres/postgresql-hardening.conf`). The `postgres` superuser password is vaulted, with break-glass retrieval only. | Operational |
| No passwords in ILM configuration | `ilm_app` authenticates with Kerberos (`gss`) as the gMSA. Production startup refuses a connection string with a password. | `PlatformSecurityTests.Secrets_in_configuration_are_refused_in_every_environment` |
| **Append-only history** | Triggers block UPDATE, DELETE and TRUNCATE on `AuditRecords` and `LeaverTransitions` for every role, including the owner. Grants add a second layer. | `PostgresPrivilegeTests`, plus the SQLite trigger test |
| **Tamper-evident** audit chaining | A SHA-256 chain plus an HMAC with a key held outside the database (see [AUDIT.md](AUDIT.md)) | `AuditChainTests`, `DatabaseSecurityTests` |
| **Off-box** audit forwarding | The worker forwards every sealed record to a JSONL file on a separate share and/or the SIEM. Forwarding lag is a health signal and raises an alert. | `DatabaseSecurityTests` |
| Access logging | `log_connections`, `log_disconnections`, `log_statement = 'ddl'`, and `pgaudit` (`ddl, role, write`) for DBA sessions. Ship PostgreSQL logs to the SIEM too. | Operational |
| **Row-level** access controls | Not needed for the application identity, because all access goes through ILM's authorisation and scopes. `ilm_auditor_ro` gets SELECT only on the audit tables. If analysts get direct read access, create views that exclude `Persons` personal fields and add PostgreSQL row-level security policies per business entity. | Documented |
| Sensitive values absent | `AuditSanitizer` runs before anything is stored, and passwords are never read. | `DatabaseSecurityTests.Sensitive_values_are_absent_from_audit_and_the_database` |

## Threats involving the DBA

The DBA is trusted with the platform, but treated as a potential insider for lifecycle history.

| Attempt | Outcome |
|---|---|
| Edit or delete audit rows | Needs a trigger to be dropped first. That DDL is logged by `log_statement` and pgaudit, and the change is detected by the verifier (hash, MAC, sequence gap, sink divergence). |
| Recompute hashes after an edit | The MAC fails, because the HMAC key isn't in the database. |
| Truncate the tail or restore an old backup | The off-box sink holds sequences the database lacks, so the verifier reports a truncation. |
| Read personal data | Possible. Mitigated by DBA access logging, named logins, and data minimisation (no HR data, no passwords, no tokens). Residual risk R4 in [THREAT-MODEL.md](THREAT-MODEL.md). |
| Grant themselves an ILM role | Not possible. Roles come from Okta groups by immutable ID, not from the database. |

## Backups

| Item | Standard |
|---|---|
| **Encrypted backups** | Use `pgBackRest` or `barman` with repository encryption (`repo1-cipher-type=aes-256-cbc`), or platform snapshots with encryption. Never keep unencrypted dumps. |
| Protected backup keys | Keep the backup encryption keys in the vault, **not** held by the DBA team alone (dual custody). Rotate them yearly and after staff changes. |
| **Backup retention** | Full backup weekly, differentials daily, WAL archiving continuous, for 35 days online. Monthly backups kept 13 months offline or immutable. Match this to the personal-data retention below. |
| **Restore testing** | Restore to an isolated host monthly. Then run `dotnet Ilm.Web.dll verify-audit` against the restored database **and** the off-box sink. Expect "valid", or a truncation report equal to the restore point's age, and record the result. |
| Restore in anger | See [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md). After any restore, compare with the sink, and re-verify every leaver that was in flight after the restore point. |

## Personal data

| Topic | Policy |
|---|---|
| Minimisation | ILM stores identifiers, names, department, business entity, link evidence and lifecycle state. It doesn't store HR records, passwords, tokens, LAPS values or mailbox content. |
| **Retention** | Person and identity records: while any account exists, plus 2 years after the final leaver completes. Leaver requests, transitions and audit: 7 years, or your records policy. Operator sign-in records: 2 years. ILM never deletes automatically (`AUTOMATIC_DELETION` is rejected). Purges run under change control, after an audit **export** of the affected range has been verified and archived. |
| Subject access and **export** | Auditors export audit data through `/Audit` → Export: CSV with formula neutralisation, and the export is itself audited. For a data subject request, export the person's records with a read-only query as `ilm_auditor_ro`, log the export, and deliver it through the data protection officer. |
| Export controls | Only Auditors can export. Exports are sanitised and audited, and should be stored only in the approved case-management location. |

## Setting up PostgreSQL

1. As the PostgreSQL administrator, run `deploy/postgres/00-create-roles-and-database.sql` with `psql -v migrator_password=… -v app_password=…`. Afterwards remove the password lines and switch to `gss` for both ILM roles.
2. Merge `postgresql-hardening.conf` into `postgresql.conf`, and apply the `pg_hba.conf` and `pg_ident.conf` examples.
3. With the migration identity, run `dotnet Ilm.Web.dll migrate`, then `psql -f deploy/postgres/20-grants-after-migration.sql`.
4. Connection strings (no passwords):
   - `ConnectionStrings:Ilm` = `Host=pgsql01.corp.example.test;Database=ilm;Username=ilm_app;SSL Mode=VerifyFull`
   - `ConnectionStrings:IlmMigration` = the same, with `Username=ilm_migrator`, supplied only in the change window

Kerberos (`gss`) from Npgsql on Windows hasn't been verified in this repository. Check it in the Phase 3 lab (see [ACTIVE-DIRECTORY.md](ACTIVE-DIRECTORY.md)).

## Development

`docker compose up -d postgres` (after `scripts/dev-env.sh`) runs PostgreSQL 16 on `127.0.0.1:5432`, with random passwords in a git-ignored `.env` and the same role scripts. The integration test `PostgresPrivilegeTests` runs when `ILM_TEST_POSTGRES` holds an admin connection string. It creates uniquely named roles and a database, applies the migrations as the migrator, and checks the grants and triggers.
