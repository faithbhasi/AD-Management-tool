# Testing

Four xUnit projects, two smoke-test runners, formatting, static analysis and secret scanning. Everything runs against mocks, SQLite and PostgreSQL. Nothing here touches a real directory or Okta org. The integrations that need those are listed under [Not covered](#not-covered) with their lab procedures.

## Suites

| Project | Tests | What it covers |
|---|---|---|
| `Ilm.UnitTests` | 175 | Pure domain and application logic. |
| `Ilm.IntegrationTests` | 67 | Real services and EF Core against a temporary SQLite database, a fake clock, the mock directory and the mock Okta org. One PostgreSQL test runs when `ILM_TEST_POSTGRES` is set. |
| `Ilm.SecurityTests` | 22 | The web host (`WebApplicationFactory`, Testing environment) through the mock OIDC provider, plus repository and platform checks |
| `Ilm.EndToEndTests` | 10 | Full HTTP journeys: sign in through mock OIDC, Razor pages and forms with antiforgery, request → approve → contain → verify, manual tasks, reconciliation, audit export, sign-out |

The unit tests cover:

- protection floor
- authority and writer conflicts
- connector modes
- leaver state machine
- SafelyContained
- identity link policy
- configuration validator
- audit chain
- LDAP filter, DN and SID helpers and error classification
- the Okta HTTP client (stub handler)
- the feasibility report
- architecture rules

Last full run (see [FINAL-REPORT.md](FINAL-REPORT.md)): **274 passed, 0 failed, 0 skipped**, including the PostgreSQL test against PostgreSQL 16 in Docker.

## Running

```bash
dotnet test Ilm.slnx                                     # everything except the PostgreSQL test
ILM_TEST_POSTGRES='Host=127.0.0.1;Port=55432;Username=postgres;Password=<local>;Database=postgres' \
  dotnet test Ilm.slnx                                   # includes PostgresPrivilegeTests
dotnet test tests/Ilm.IntegrationTests --filter "FullyQualifiedName~LeaverScenarioTests"
scripts/run-smoke.sh                                     # 19 HTTP checks, SQLite, fresh App_Data
scripts/run-smoke-postgres.sh                            # the same 19 checks on docker-compose PostgreSQL,
                                                         # ilm_migrator for migrations, ilm_app at runtime
```

For the PostgreSQL test, use a disposable server. It creates uniquely named roles and a database and drops them afterwards. For example:

```bash
docker run -d --name ilm-test-pg -e POSTGRES_PASSWORD=<random> -p 127.0.0.1:55432:5432 postgres:16-alpine
```

## Quality gates

| Gate | Command |
|---|---|
| Formatting | `dotnet format Ilm.slnx --verify-no-changes` |
| Static analysis | `dotnet build Ilm.slnx -c Release --no-incremental -warnaserror`. `TreatWarningsAsErrors`, `AnalysisLevel=latest-default`, code style is enforced in the build, and the security CA rules (CA2100, CA3001, CA3003, CA3006, CA3075, CA3147, CA5350, CA5351, CA5359, CA5379, CA5384 and others) are errors in `.editorconfig`. |
| Secret scanning | `detect-secrets scan --all-files --exclude-files '(^|/)(bin|obj|App_Data|\.git)/|^\.env$'` plus `Repository_contains_no_real_secrets_or_real_domains` |
| Diagram syntax | Each Mermaid block rendered with `@mermaid-js/mermaid-cli` |

## Traceability to the brief (§21)

| Requirement | Tests |
|---|---|
| **Architecture:** overlapping writers rejected | `AuthorityTests.Overlapping_writers_with_identical_keys_are_rejected`, `Incomparable_overlapping_rules_with_different_owners_are_rejected`, `Validator_rejects_overlapping_writers_and_missing_containment_owner` |
| Missing authority creates manual containment | `LeaverScenarioTests.Unresolved_authority_creates_manual_runbook_alert_and_sla` |
| Legacy containment works without a target identity | `Legacy_ad_authoritative_user_without_target_identity_is_contained_by_containment_only_legacy` |
| Granting actions fail closed | `FeasibilityHarnessTests.Okta_provisioning_strategy_stays_disabled_without_approved_feasibility`, `Initially_enabled_strategies_are_exactly_read_only_containment_only_legacy_and_manual`, `ConnectorModePolicyTests` |
| Containment requests never disappear silently | `Containment_request_never_disappears_when_validation_fails`, `Failure_before_any_change_is_recorded_then_falls_back_to_manual` |
| **Protection:** hardcoded SID can't be removed | `ProtectionFloorTests.Hardcoded_floor_cannot_be_removed_by_configuration`, `Floor_source_cannot_be_declared_in_configuration` |
| `adminCount` object denied | `AdminCount_object_is_denied` |
| Recursive protected membership denied | `Recursive_protected_membership_is_denied` |
| Foreign security principal handled | `Foreign_security_principal_membership_in_other_forest_is_denied`, `ProtectionAndViewsTests.Foreign_security_principal_is_resolved_across_forests` |
| Runtime identity denied, gMSA retrieval principal protected | `Runtime_identity_is_denied`, `Gmsa_password_retriever_is_protected_directly_and_through_group` |
| Protected server denied | `Protected_servers_are_denied` |
| Attack-path import adds protection | `Attack_path_import_adds_protection` |
| Unknown protection state denied | `Unknown_protection_state_is_denied`, `Unavailable_forest_makes_cross_forest_protection_unknown` |
| **Authentication:** correct issuer and subject | `AuthenticationTests.Operator_is_keyed_by_exact_issuer_and_subject`, `Tokens_with_wrong_issuer_audience_or_signature_are_rejected` |
| Changed email doesn't create a new identity | `Changed_email_does_not_create_a_new_identity` |
| Issuer change treated as identity migration | `Issuer_change_is_an_identity_migration_requiring_security_approval` (pending status, no roles, dual control, and self-approval across issuers refused) |
| Mutable group name doesn't grant a privileged role | `Mutable_group_name_in_token_does_not_grant_a_privileged_role`, smoke check "renamed group name grants no role" |
| Server-side membership failure denies a privileged role | `Server_side_membership_failure_denies_privileged_roles` |
| **Leaver:** Okta-authoritative, legacy-AD-authoritative and target-AD-authoritative users | `Okta_authoritative_user_is_contained_through_okta_and_ad_is_verified_not_written`, `Legacy_ad_authoritative_user_without_target_identity_…`, `Target_ad_authoritative_user_falls_back_to_manual_while_direct_ad_is_disabled` |
| Unresolved authority, no target identity, two identities during coexistence | `Unresolved_authority_…`, `Legacy_ad_…_without_target_identity_…`, `Two_identities_during_coexistence_are_both_contained` |
| Already-disabled account, protected account | `Already_disabled_account_is_verified_without_a_write`, `Protected_or_unknown_accounts_are_never_written_and_get_tier0_tasks` |
| Session revocation failure, AD disable failure, partial containment | `Session_revocation_failure_requires_manual_action_and_blocks_safely_contained`, `Okta_session_revocation_failure_is_not_silently_ignored`, `Ad_disable_failure_falls_back_to_manual_then_verifies`, `Change_that_cannot_be_verified_is_partially_contained` |
| Exact SafelyContained requirements | `SafelyContainedEvaluatorTests` (8), `Provisional_link_prevents_safely_contained_until_rejected`, `Provisional_link_approved_after_approval_becomes_a_verified_manual_containment`, `Account_without_a_link_record_blocks_safely_contained_until_resolved` |
| Repeated idempotency key, concurrent requests | `Repeated_idempotency_key_returns_the_same_request_and_conflicting_payload_is_refused`, `Concurrent_requests_for_one_person_produce_exactly_one_active_leaver` |
| Expired approval, changed plan invalidates approval | `Expired_approval_cannot_be_used`, `Changed_plan_invalidates_prior_approval` |
| **Okta feasibility:** staged user, assignment, AD provisioning, delay, duplicate Okta user, session revocation, agent outage, retry without duplicates | `Full_mock_run_exercises_all_26_checks_and_recommends_no_go` (asserts each check's outcome) |
| No AD provisioning | `No_ad_provisioning_is_reported_as_failure_not_assumed` |
| Duplicate AD user | `Duplicate_ad_user_is_matched_not_duplicated` |
| Delayed asynchronous provisioning | `Delayed_asynchronous_provisioning_is_measured_not_hardcoded` |
| Push overwrite, deactivation propagation | `Push_overwrite_is_detected_as_a_condition`, `Deactivation_that_does_not_propagate_fails_with_a_condition` |
| Mock evidence can't be approved or passed off as PCATEST | `Feasibility_service_stores_report_and_mock_cannot_be_submitted`, `Pcatest_run_refuses_the_simulated_okta_org_and_mock_directory`, `FeasibilityReportTests` |
| **Database:** encrypted connection required in production | `DatabaseSecurityTests.Production_requires_encrypted_postgresql_connection`, `PlatformSecurityTests.Production_refuses_development_shortcuts` |
| Application identity has no schema-owner rights | `PostgresPrivilegeTests.Application_identity_has_dml_only_and_history_is_append_only` (real PostgreSQL), `Database_grants_give_the_application_no_schema_owner_rights` |
| Audit chain modification detected | `AuditChainTests`, `Dba_who_bypasses_triggers_and_rewrites_history_is_detected`, `Persisted_audit_chain_verifies_against_the_off_box_copy` |
| Backup configuration documented | `Backup_and_dba_risk_are_documented` |
| Sensitive values absent | `Sensitive_values_are_absent_from_audit_and_the_database`, `Secrets_in_configuration_are_refused_in_every_environment` |

## Smoke test checks

`scripts/smoke_test.py` refuses any host other than `localhost`. It checks:

1. liveness
2. readiness
3. the OIDC challenge for anonymous users
4. development sign-in
5. roles resolved on the server
6. that a renamed decoy group grants nothing
7. audit denied to non-auditors
8. user search
9. that computer search is limited to the operator's scope
10. person search
11. the leaver plan preview
12. a leaver request
13. that the requester can't see the approve button
14. that the Security Approver can
15. approval
16. that a legacy-only leaver reaches SafelyContained
17. audit read by the auditor
18. audit chain verification
19. CSV export

## Not covered

These can't run here. The procedures that close each gap:

| Area | Procedure |
|---|---|
| LDAP against real DCs, gMSA bind, LDAPS validation, compare-and-swap, RODC refusal | [ACTIVE-DIRECTORY.md](ACTIVE-DIRECTORY.md#lab-validation-procedure-phase-3) L1–L15 |
| Okta management API against a real org, `private_key_jwt` with a machine-store key | PCATEST run ([OKTA-AD-PROVISIONING.md](OKTA-AD-PROVISIONING.md#running-it-in-pcatest-not-run-in-this-repository)) |
| Okta OIDC against a real authorisation server | Sign in to the pilot, check the Me page (roles and their source) |
| Windows, IIS, ANCM and the gMSA app pool, the PowerShell deployment scripts | Lab host build ([DEPLOYMENT.md](DEPLOYMENT.md)) |
| Npgsql Kerberos (`gss`) to PostgreSQL | Lab, with the `pg_hba` example ([DATABASE-SECURITY.md](DATABASE-SECURITY.md)) |
| Entra, Citrix and VPN session revocation | Not implemented. These are `NotConfigured` connectors that produce manual tasks. |
| Browser automation | The end-to-end tests drive HTTP and HTML directly. No browser test was run. |
