# Change management

ILM is a Tier 1 system that can remove access across forests. Changes go through two channels:

- **Code and deployment** go through source control and releases.
- **Behaviour**, meaning authority, protection, connector modes, policies and flags, goes through versioned, dual-controlled configuration inside ILM.

Neither channel can enable Tier 0 management.

## Roles

| Role | Holds |
|---|---|
| Service owner | Priorities, SLAs, go/no-go for phases |
| Security architect | Threat model, protection floor changes (code), vulnerability reports |
| Configuration Administrator (ILM role) | Proposes configuration versions |
| Security Approver (ILM role) | Approves configuration, feasibility reports, issuer migrations, rollbacks |
| AD engineer | gMSA, delegation, lab |
| Okta engineer | OIDC app, service app, role groups, PCATEST |
| DBA | PostgreSQL platform, backups, restore tests. Holds no ILM role. |

No one should hold both Configuration Administrator and Security Approver. ILM would still refuse self-approval, but separate holders keep the review meaningful.

## Code changes

1. Open a pull request. It needs at least one reviewer who didn't write it. Changes to any of these also need the security architect's review:
   - `Ilm.Domain/Protection`
   - `ConnectorModePolicy`
   - `GuardedDirectoryWriter`
   - authentication
   - the audit chain
   - the validator
   - migrations
2. CI (or the local equivalent):
   - `dotnet format --verify-no-changes`
   - `dotnet build -warnaserror` (analysers, with security rules as errors)
   - all four test projects
   - `PostgresPrivilegeTests` with `ILM_TEST_POSTGRES`
   - `detect-secrets scan`
   - the smoke test (`scripts/run-smoke.sh` and `scripts/run-smoke-postgres.sh`)
3. The release records the commit, the build output hash and the test results.
4. Deploy in a change window ([DEPLOYMENT.md](DEPLOYMENT.md)). Database migrations run as `ilm_migrator`, followed by the grants script.
5. Post-deployment: readiness, `verify-audit`, sign-in for each role.

### Changes needing extra scrutiny

| Change | Requirement |
|---|---|
| Protection floor (add a RID, SID, flag or class) | Security architect approval. **Removing** anything needs a documented threat analysis and the service owner's sign-off. |
| New write operation, or a connector mode's permissions | Threat model update and lab validation first |
| Enabling a disabled capability's code path (joiner, password reset, moves) | A new phase plan, a lab and pilot, and then only through a configuration flag |
| Audit canonical form or MAC | A new key ID or version marker, so that old records still verify |

## Configuration changes

These go through propose → validate → Security Approver approval → activate → versioned, with a rollback target ([APPROVALS.md](APPROVALS.md)).

- Every proposal carries a change reference in its summary.
- The first version is created once with `bootstrap-config`, from a document reviewed by two people in source control.
- Keep the source-controlled copy of the active configuration in step: export it from Admin → Configuration after each activation.

## Phase gates

| Gate | Evidence required |
|---|---|
| Phase 3 → pilot (Phase 5) | Lab procedure L1–L14 passed and recorded ([ACTIVE-DIRECTORY.md](ACTIVE-DIRECTORY.md#lab-validation-procedure-phase-3)). Restore test passed. Threat model reviewed. Okta role groups set up with immutable IDs. |
| Enable a legacy forest for `ContainmentOnlyLegacy` | Delegation applied to named OUs only. SIEM alert on 4722/4738 by the gMSA. A Security Approver approves the configuration version. |
| Enable `OktaUserProvisioning` / `OktaToAdProvisioning` (Phase 6) | An approved PCATEST feasibility report (Go or Conditional Go), with every condition implemented. Then a configuration version referencing the report's run ID and hash, approved by a Security Approver. |
| Enable `DirectActiveDirectoryContainment` or `WriteTarget` | Lab validation of the direct AD path. Delegation limited to `userAccountControl` on named OUs. Security Approver approval. |
| Any Tier 0 capability | **Not available.** No flag exists, and unknown flags are rejected. |

## Emergency changes

- **Revert a bad configuration.** Propose a rollback to the previous version. It needs the same approval, but can be decided in minutes.
- **Stop all automated writes at once.** Propose a version with `LegacyContainment=false` and `OktaContainment=false`. Containment then becomes manual tasks, and nothing is silently skipped.
- **Code hotfix.** Normal PR and review, with an expedited window. Security-sensitive files still need the security architect.

## Records

Keep these for the audit retention period:

- change tickets
- PR links
- release hashes
- configuration versions (which are also in ILM)
- feasibility reports and their approvals
- lab results
- restore-test results
