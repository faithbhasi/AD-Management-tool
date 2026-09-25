# Final report

This report says only what was **executed** in this environment and what the result was. Anything that couldn't be run is listed as **unverified**, together with the procedure that would verify it.

## Environment

| Item | Value |
|---|---|
| OS | Linux 6.18 (cloud container) |
| .NET SDK | 10.0.112 (.NET 10 LTS) |
| Docker | 29.3.1 with Compose v5.1.1 |
| PostgreSQL | 16.15 (`postgres:16-alpine`), in two containers: a disposable test server, and the `docker-compose.yml` dev service |
| Python | 3.11.15 (smoke test) |
| Secret scanner | `detect-secrets` 1.5.0 |
| Diagram check | `@mermaid-js/mermaid-cli` 11.17.0 with the pre-installed Chromium |
| **Not available** | Active Directory or any domain controller, a Windows Server, IIS or gMSA, the Okta PCATEST or any real Okta org, Entra, Citrix, VPN, a SIEM, and a web browser for UI automation |

## Execution steps (brief §24)

| # | Step | Result |
|---|---|---|
| 1–2 | Inspect the repository and preserve existing files | `docs/architecture.md`, `docs/leaver-containment.md` and `docs/open-questions.md` are kept unchanged. `README.md` was rewritten to cover the built application, with the original document table kept. |
| 3–4 | PLAN, ASSUMPTIONS, threat model | Written ([PLAN.md](PLAN.md), [ASSUMPTIONS.md](ASSUMPTIONS.md) A1–A22, [THREAT-MODEL.md](THREAT-MODEL.md)) |
| 5 | Compact solution | 7 `src` and 4 `tests` projects exactly (`ArchitectureRulesTests.Solution_stays_compact`) |
| 6–8 | Phases 0, 1, and 2 with mocks | Implemented (see [ARCHITECTURE.md](ARCHITECTURE.md)) |
| 9 | PCATEST feasibility harness | Implemented with 26 checks. **Run in mock mode only.** |
| 10 | Migrations | SQLite and PostgreSQL migrations (initial schema, append-only triggers, transition ordinal). Applied successfully to both engines. |
| 11 | Fictional development data | 3 forests (`corp`, `legacy-a`, `legacy-b` under `example.test`), 13 people, 7 operators, fictional configuration |
| 12 | Tests | 274 tests in 4 projects, plus 2 smoke runners |
| 13 | Formatting | `dotnet format Ilm.slnx --verify-no-changes`: **exit 0**. The first run found whitespace and import-order issues in 7 files. They were fixed with `dotnet format`, and the check re-run clean. |
| 14 | Static analysis | `dotnet build Ilm.slnx -c Release --no-incremental -warnaserror`: **0 warnings, 0 errors** (analysers at `latest-default`, security CA rules as errors) |
| 15 | Secret scanning | `detect-secrets scan --all-files` (excluding `bin`, `obj`, `App_Data`, `.git`, `.env`): 8 hits, **all false positives**, listed below. The in-repo `Repository_contains_no_real_secrets_or_real_domains` test passed. |
| 16 | Build | Passed (Debug and Release) |
| 17 | All available tests | **274 passed, 0 failed, 0 skipped**, with `ILM_TEST_POSTGRES` set, so the PostgreSQL test ran |
| 18 | Start the application | Started in Development on `https://localhost:5001`, twice: with SQLite, and with PostgreSQL as `ilm_app` after migrations as `ilm_migrator` |
| 19 | Smoke tests | `scripts/run-smoke.sh` (SQLite): **19/19**. `scripts/run-smoke-postgres.sh` (PostgreSQL, separate identities): **19/19**, and `verify-audit` reported a valid chain. |
| 20 | Fix failures | See "Defects found and fixed" below |
| 21 | This report | — |

Also executed:

- CLI verbs `migrate`, `bootstrap-config` (valid, invalid, repeated, and an unresolvable scope), `check-config` (valid, and an unsafe document rejected with 3 codes), `verify-audit`, `export-dev-config`, and `feasibility --mode mock`
- `docker compose config` and `up` for the PostgreSQL service
- rendering of all 16 Mermaid diagrams

### Test results by project

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| Ilm.UnitTests | 175 | 0 | 0 |
| Ilm.IntegrationTests (including `PostgresPrivilegeTests` on PostgreSQL 16.15) | 67 | 0 | 0 |
| Ilm.SecurityTests | 22 | 0 | 0 |
| Ilm.EndToEndTests | 10 | 0 | 0 |
| **Total** | **274** | **0** | **0** |

Without `ILM_TEST_POSTGRES`, the PostgreSQL test reports as skipped (273 passed, 1 skipped).

### Secret-scan findings (all reviewed, none real)

| File | Why it matched | Assessment |
|---|---|---|
| `src/Ilm.Web/appsettings.json` | `ClientSecretEnvironmentVariable: "ILM_OIDC_CLIENT_SECRET"`, which is the *name* of an environment variable | Not a secret |
| `deploy/postgres/postgresql-hardening.conf` | `password_encryption = 'scram-sha-256'` | Setting |
| `src/Ilm.Application/Okta/OktaModels.cs` | The Okta status constant `PASSWORD_EXPIRED` | Enum text |
| `src/Ilm.Web/Cli/CommandRunner.cs` | The word "password" in the mock feasibility notes | Prose |
| `TROUBLESHOOTING.md` | "random development passwords" | Prose |
| `tests/…/DatabaseSecurityTests.cs`, `tests/…/AuditChainTests.cs` (×2) | **Deliberately fake** values (`S3cr3t-Hunter2-Value`, a dummy JWT) used to prove redaction | Test fixtures |

Development secrets (the database passwords in `.env`, the audit key in `App_Data/audit-dev.key`, and the mock OIDC key and secret, which live only in memory) are generated randomly at run time and git-ignored.

## Defects found and fixed while verifying

Running the full stack against real PostgreSQL, and checking each documented claim against the code, turned up these defects. Each fix has a test that fails without it.

| # | Defect | Fix | Test |
|---|---|---|---|
| 1 | **Audit chain broke on PostgreSQL.** .NET timestamps have 100 ns ticks and `timestamptz` stores microseconds, so most records failed verification once read back. The PostgreSQL test had compared tracked in-memory entities, so it missed this. | `AuditChain.Seal` truncates to microseconds before hashing. The PostgreSQL test now verifies records read through a fresh context. | `Sealed_timestamp_survives_a_microsecond_precision_database_round_trip`, `PostgresPrivilegeTests` (confirmed failing without the fix), PostgreSQL smoke test |
| 2 | A provisional link **approved after** the leaver was approved produced no containment action, so the leaver could never reach SafelyContained. An account with **no link record** didn't block SafelyContained at all. | Unresolved links are tracked per account, including accounts with no link record. A late-confirmed account gets a *manual* containment action, not an automated write outside the approved plan hash, and ILM verifies it by reading the directory. | `Provisional_link_approved_after_approval_becomes_a_verified_manual_containment`, `Account_without_a_link_record_blocks_safely_contained_until_resolved` |
| 3 | **Issuer change:** the new identity got roles straight away, and the old and new identities counted as different people, so an operator could approve their own pre-change request. | A new identity with the same subject under a new issuer is `PendingIssuerMigration` with no roles until the migration is approved and applied. Separation-of-duties checks follow the migration links, for approvals and identity links. | `Issuer_change_is_an_identity_migration_requiring_security_approval` (extended) |
| 4 | A run labelled PCATEST could use the **simulated Okta org or a mock directory**, producing "evidence" that could be approved. | `FeasibilityService` refuses PCATEST mode while any simulated control is registered, or when the target connector isn't `Ldap`. | `Pcatest_run_refuses_the_simulated_okta_org_and_mock_directory` |
| 5 | Production had **no way to create the first configuration version**. Only the development seeder could, and without a configuration no one gets roles. Scopes on a **newly added connector** were skipped by directory validation. | New `bootstrap-config --file --change` verb with full validation. It works only while no configuration exists, and audits the change ticket and file hash. Validation builds readers from the proposed document's own connectors. | `Bootstrap_only_creates_the_first_version_and_needs_a_source_reference`, `Scopes_on_a_newly_added_connector_are_verified_against_that_directory`, CLI runs |
| 6 | Secrets placed directly in configuration weren't refused, although SECURITY.md said they were. | `StartupValidator` refuses secret-named keys with values in every environment, and password-bearing connection strings in production. | `Secrets_in_configuration_are_refused_in_every_environment` |
| 7 | Audit forwarding lag degraded health but raised **no alert**, although the threat model said it did. | The worker raises a High `AuditForwardingLag` alert, once while unacknowledged. | `Forwarding_lag_beyond_the_threshold_raises_one_alert` |
| 8 | CLI verbs crashed with an unhandled exception on a missing argument | Usage message and exit code 2 | CLI runs |
| 9 | The JSONL sink reader let a later duplicate entry override the first | The first entry per sequence wins | Covered by the existing sink verification tests |

Defects fixed earlier in the build are recorded in the commit history. One example is the sign-out form missing its antiforgery token, found by the end-to-end tests.

## Acceptance criteria (brief §25)

| Criterion | Status | Evidence |
|---|---|---|
| Solution builds | **Met** | Release build, `-warnaserror`, 0 warnings |
| Tests pass except documented environmental tests | **Met** | 274/274. None skipped in the final run. |
| Application runs locally | **Met** | Started on SQLite and on PostgreSQL. Both smoke runs 19/19. |
| Development login works | **Met** | Mock OIDC (code + PKCE S256), smoke check 4, `AuthenticationTests` |
| No real secrets exist | **Met** | Secret scan above, plus the repository test |
| Person and account are separate | **Met** | `Person`, `ExternalIdentity` and `IdentityLink`. See [IDENTITY-LINKING.md](IDENTITY-LINKING.md). |
| Email-only links remain provisional | **Met** | `IdentityLinkPolicyTests`, Harper scenario |
| Source of authority is attribute-set aware | **Met** | 12 attribute sets, `AuthorityResolver`, `AuthorityTests` |
| AccountEnabledState has an explicit owner | **Met** | `CONTAINMENT_OWNER_MISSING` validator. The containment owner is recorded on each action. |
| Overlapping writers are rejected | **Met** | `WriterConflictDetector`, `OVERLAPPING_WRITERS` |
| Leavers without target accounts can be contained in legacy AD | **Met with mocks** | Casey scenario, smoke check 16. **Real LDAP unverified.** |
| Unresolved containment produces a manual runbook and alert | **Met** | `Unresolved_authority_creates_manual_runbook_alert_and_sla` |
| SafelyContained requires verified authentication, session and directory controls | **Met** | `SafelyContainedEvaluatorTests`, leaver scenarios |
| Hardcoded Tier 0 floor can't be removed | **Met** | `ProtectionFloorTests`. There's no configuration field for it. |
| Authority and protection changes require dual control | **Met** | `ConfigurationWorkflowTests` |
| Database threat model includes backups and DBA risk | **Met** | [THREAT-MODEL.md](THREAT-MODEL.md), [DATABASE-SECURITY.md](DATABASE-SECURITY.md), `Backup_and_dba_risk_are_documented` |
| Audit records are tamper-evident and forwardable off-box | **Met** | Hash chain plus HMAC. The JSONL sink is compared by the verifier. Tested on SQLite and PostgreSQL. **Forwarding to a real SIEM is unverified.** |
| Okta provisioning remains disabled until feasibility approval | **Met** | Validator, `Okta_provisioning_strategy_stays_disabled_without_approved_feasibility`, PCATEST guard. The mock report is No-Go. |
| The portal doesn't require Domain Admin | **Met by design, unverified in AD** | Negotiate bind as the gMSA with narrow delegation (`Grant-IlmDelegation.ps1`). Lab L1 and L6 would prove it. |
| The portal never collects an operator AD password | **Met** | No password inputs (`No_operator_password_form_and_no_bind_password_exist`). Okta OIDC only. |
| Tier 0 operations can't be enabled through configuration | **Met** | `FEATURE_UNKNOWN`, `Tier0_management_cannot_be_enabled_through_the_ui`, `Tier0_operations_cannot_be_enabled_through_configuration` |

## Unverified external integrations

These are implemented, but were **not executed** against the real system because it wasn't available. Treat them as unverified until the named procedure has been run and recorded.

| Integration | Code | How to verify |
|---|---|---|
| Active Directory over LDAP as the gMSA (LDAPS or sign and seal, `<GUID=>` addressing, in-chain groups, FSPs, compare-and-swap `userAccountControl`, RODC refusal) | `LdapDirectoryConnector`, `LdapConnectionFactory` | [ACTIVE-DIRECTORY.md → lab procedure](ACTIVE-DIRECTORY.md#lab-validation-procedure-phase-3), L1–L15 |
| gMSA creation, delegation and IIS deployment scripts | `deploy/windows/*.ps1`, `web.config` | Lab host build ([DEPLOYMENT.md](DEPLOYMENT.md)), each script with `-WhatIf` first |
| Okta OIDC against a real authorisation server | `OidcSetup` | Pilot sign-in, the Me page showing roles and their source |
| Okta management API with `private_key_jwt` and a non-exportable key | `OktaApiClient`, `PrivateKeyJwtTokenProvider` (unit-tested with a stub handler only) | PCATEST ([OKTA-AD-PROVISIONING.md](OKTA-AD-PROVISIONING.md)) |
| Okta-to-AD provisioning behaviour | Feasibility harness (mock run only) | A PCATEST run. [OKTA-AD-FEASIBILITY-REPORT.md](OKTA-AD-FEASIBILITY-REPORT.md) is a **mock** report and not evidence. |
| Npgsql Kerberos (`gss`) to PostgreSQL | Configuration only (A22) | Lab, with the `pg_hba` and `pg_ident` examples |
| SIEM forwarding over HTTPS | `HttpSiemAuditForwarder` | Point it at a test collector and compare with `verify-audit` |
| Entra, Citrix and VPN session revocation | `NotConfiguredSessionConnector` (manual tasks) | Not implemented. Needs design (open question V12). |
| UI in a real browser | — | The end-to-end tests exercised HTTP and HTML only. No browser was available. |

## Open items for the next phase

1. Run the Phase 3 lab (L1–L15). Record the results and update A5, A11, A12, A15 and A22.
2. Decide the Okta authorisation server (V6), then create the OIDC and service apps in PCATEST.
3. Run the PCATEST feasibility harness and submit the report for Security Approver review.
4. Design and implement the Entra, Citrix and VPN session connectors (V12, V15).
5. Confirm the RTO, RPO and SLAs proposed in [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md) and [MANUAL-CONTAINMENT.md](MANUAL-CONTAINMENT.md) (Q2).
