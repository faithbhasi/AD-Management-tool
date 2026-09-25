# ILM Portal: architecture baseline

**Status:** draft for review, revision 3. It amends spec v2 up to and including section 7 ("protected-object"). Spec v2 from section 7 onward has not been reviewed yet (see [open questions](open-questions.md#q1)).

This document records the design decisions from two architecture reviews of the ILM Portal spec and the follow-up on cloud orchestration. Hand it to an implementer alongside spec v2. Where the two conflict, this document wins.

## How claims are labelled

Design decisions here are proposals for review, not vendor-confirmed behaviour. Statements about vendor behaviour carry a tag:

| Tag | Meaning |
|---|---|
| **[Documented]** | Vendor documentation states it. Sources are at the end. |
| **[Verify]** | Expected behaviour that hasn't been confirmed. Test it in PCATEST, or confirm it with the vendor, before encoding it. |
| **[Assumption]** | Depends on your licensing or tenant configuration. |

Every [Verify] and [Assumption] item has a matching entry in [open-questions.md](open-questions.md).

---

## 1. Context

- **Directories.** Several legacy forests (including EIS, PCUK, PCAAUS and CCM) are being consolidated into one new target forest. Users are recreated in the target forest and keep their email addresses.
- **Workforce IdP.** Okta. An Okta → AD push pilot runs in PCATEST and places users into OUs by department.
- **Downstream systems named so far:** Microsoft 365 / Entra ID (Entra Connect is not used), Mimecast (profile groups are kept in sync by an hourly Power Automate poll today), and Citrix.
- **No HR feed** has been named. Joiners are raised by IT.

## 2. Principles

These override any conflicting rule in spec v2.

1. **One writer per attribute set, per population.** Every attribute set (see §3.2) has exactly one owning system for each population.
2. **Failure direction depends on the action.** Actions that grant access (create, enable, add to group, change attributes) fail closed. Actions that remove access (containment) never fail silently. They fall back to a manual, alerted, timed path. See [leaver-containment.md §6](leaver-containment.md#6-failure-direction).
3. **ILM is Tier 1.** It never acts on a Tier 0 object and never holds a Tier 0 right (§7).
4. **No stored directory secrets, and no AD password form.** The runtime identity is a gMSA, and operators sign in only through Okta (§5, §6).
5. **Audit leaves the box.** Audit records are append-only and forwarded to the SIEM, so no single administrator can erase them (§11).
6. **A step is done when its effect has been observed, not when an API returned success.** Where an effect can't be observed, the record says so (see verification levels in [leaver-containment.md §5](leaver-containment.md#5-verification)).

## 3. Source of authority

### 3.1 Decision: option B, conditional on the pilot reaching production

| Population | Owner of the account | How ILM writes | Notes |
|---|---|---|---|
| Standard users, target forest | **Okta** (Okta pushes to AD) | Okta API only. ILM never writes the AD attributes Okta maps. | ILM adds templates, approvals and uniqueness checks that the Okta console lacks. |
| Admin accounts, target forest (Tier 1/2 only) | **ILM** | LDAP, directly | Never Okta-pushed. This keeps the direct write path small and auditable. |
| Computer objects, target forest (non-Tier 0) | **ILM** | LDAP, directly | Tier 0 servers are excluded (§7.4). |
| Anything in a legacy forest | Legacy AD (read-only to ILM) | None, except the containment-only exception | See §4 and [leaver-containment.md §7](leaver-containment.md#7-containment-only-legacy-exception-amends-spec-v2-rule-6). |
| Tier 0 objects, any forest | Manual process on a PAW | None | ILM raises a task and verifies the result afterwards. |

**If the pilot doesn't reach production**, or joiners stay manual and IT-driven with no HR feed, fall back to **option A**: ILM writes AD for standard users too, and Okta imports from AD. Option A is simpler, but you have to abandon the Okta → AD push direction, and leaver timing then depends on the import schedule.

**Rejected: both systems writing the same population.** That produces dual mastering, overwrite races and duplicate profiles.

### 3.2 Attribute-ownership sets

Ownership is declared per set rather than per object, because containment needs a different owner from profile data:

| Set | Contents | Owner for Okta-mastered users | Owner for ILM-mastered accounts |
|---|---|---|---|
| Profile | Names, title, department, manager, and the other attributes in the Okta push mapping | Okta | ILM |
| Enabled state | `userAccountControl` ACCOUNTDISABLE bit | Okta, with a containment override (below) | ILM |
| Group membership | Security and distribution groups | Okta group push | ILM |
| OU placement | Parent container | **Undecided.** Okta places users by department at create time. Whether it also moves them on a department change is [Verify] ([V4](open-questions.md#v4)). | ILM |
| Password | `unicodePwd`, `pwdLastSet` | Depends on the Okta AD agent configuration ([V5](open-questions.md#v5)) | ILM |

**The containment override** is the only exception to principle 1. When the owner of the enabled-state set is Okta and Okta's deactivation hasn't produced a disabled AD account within the SLA, ILM may set the ACCOUNTDISABLE bit directly. It needs Security Approver approval and raises a drift incident. It only ever disables, never enables. Details are in [leaver-containment.md §4](leaver-containment.md#4-containment-owner-per-account).

Before any code is written, list the attributes in the Okta push mapping ([V2](open-questions.md#v2)) and check the Okta AD import matching rules ([V3](open-questions.md#v3)). The profile set is exactly that list.

## 4. Forest scope

- **Target forest:** read and write, following §3.
- **Legacy forests:** read-only connectors. They exist for:
  - uniqueness lookups (§8)
  - identity linking (§10)
  - the containment-only exception
- **Spec v2 rule 6 still governs every other legacy write** (migration-transition state plus a conclusive target link).

## 5. Operator sign-in and authorisation

- **Protocol:** Okta OIDC. ILM is a confidential client using the authorization code flow with PKCE.
- **Authenticator:** the ILM app's authentication policy requires a phishing-resistant authenticator.
- **Identity key:** issuer + `sub`. Choose between the org authorization server and a custom one before the first deployment, because the issuer is part of every stored operator identity. Switching later re-keys all of them. A custom authorization server is an [Assumption] about your licensing ([V6](open-questions.md#v6)).
- **Roles aren't read from token group names.** Okta group claims are normally built with a name filter, and group names can be changed. ILM resolves role membership server-side from immutable Okta group IDs, or from app assignment, keyed on `sub`. It re-checks privileged roles when an action is committed, not only at sign-in.
- **Removed:** the AD username/password form and Windows Integrated Authentication. A form-based bind to AD bypasses MFA.
- **Break-glass:** a separate, vaulted local credential. Retrieving it needs two people, every use raises an alert, and it isn't an AD password. See [V14](open-questions.md#v14).
- **Trust boundary:** anyone who can change ILM's Okta app assignments or role groups controls ILM. Those Okta admin rights must be managed at least as tightly as Tier 1.

## 6. Hosting and runtime identity

- **Host:** Windows Server, managed as a Tier 1 asset. Containers are for local development only.
- **Runtime identity:** a gMSA. LDAP uses Kerberos sign-and-seal or LDAPS. **[Documented]** Setting `unicodePwd` requires a 128-bit encrypted connection: TLS, or an encrypted SASL (Kerberos/NTLM) session.
- **Who can retrieve the gMSA password:** `PrincipalsAllowedToRetrieveManagedPassword` holds the ILM host(s) only. Anyone on that list effectively holds ILM's power. Monitor the attribute for changes.
- **Delegation:** ACLs on the scoped target OUs only. No rights on the domain root, AdminSDHolder, the Domain Controllers OU or GPOs.
- **Cloud API credentials** (Okta, Graph, Mimecast, Azure Automation) are secrets, even though AD needs none. Prefer asymmetric credentials: an Okta OAuth service app using `private_key_jwt`, and certificate credentials for Entra apps. Keep private keys non-exportable. Grant the Okta service app least-privilege scopes and a custom admin role limited to the resources it manages.
- **Reverse proxy and database** belong to the same tier as the host.

## 7. Tier 0 protection

### 7.1 Hard floor (in code, not configuration)

No configuration change can remove these. ILM can read them, but it refuses to modify them, act on their members (recursively), or add anyone to them:

| Kind | Well-known identifiers |
|---|---|
| Domain groups (RID) | 512 Domain Admins, 516 Domain Controllers, 518 Schema Admins, 519 Enterprise Admins, 498 Enterprise Read-only Domain Controllers, 521 Read-only Domain Controllers, 526 Key Admins, 527 Enterprise Key Admins, 520 Group Policy Creator Owners |
| Builtin groups (SID) | S-1-5-32-544 Administrators, -548 Account Operators, -549 Server Operators, -550 Print Operators, -551 Backup Operators, -552 Replicator |
| Accounts (RID) | 500 Administrator, 502 krbtgt |
| Self | ILM's own gMSA, the ILM host computer objects, and the groups that grant ILM's delegation |

Spec review v2 proposed six of these (544, 512, 518, 519, 498, 516). The additions cover the rest of the AdminSDHolder-protected set **[Documented]**, plus Group Policy Creator Owners, whose members can create GPOs. DnsAdmins has no fixed RID. Add it to the configured list by SID for each domain.

### 7.2 Protected-object rules

- **Any object with `adminCount=1` is refused.** AdminSDHolder/SDProp resets the ACLs on protected objects, so OU delegation doesn't apply to them anyway. **[Documented]** `adminCount` stays at 1 after an account leaves a protected group. ILM still refuses (fail-safe) and raises a clean-up task for a Tier 0 operator.
- **Recursive membership** is resolved with `LDAP_MATCHING_RULE_IN_CHAIN` (`1.2.840.113556.1.4.1941`) **[Documented]**. Cross-forest members appear as foreign security principals. Resolve them against the source forest's read-only connector.

### 7.3 Permission-based Tier 0

Tier 0 also comes from permissions, not only group membership. Examples:

- replication (DCSync) rights
- control of the domain root or AdminSDHolder
- ownership of GPOs linked to the Domain Controllers OU
- unconstrained delegation
- the right to retrieve ILM's gMSA password

ILM doesn't compute attack paths itself. A periodic attack-path analysis produces an export, which is imported as an approved configuration version (§7.5) and feeds the protected lists.

### 7.4 Tier 0 computers

Extends spec v2 rule 9. These are protected as computer objects:

- domain controllers
- certificate authority servers
- Okta AD agent hosts
- ADFS servers, if present
- Tier 0 PAWs
- the ILM hosts themselves (self-protection)

### 7.5 Changing protection or authority rules

- Needs dual control: a Security Approver who isn't the requester.
- Is versioned as `ApprovedConfigurationVersion` and audited.
- Can't go below the hard floor.

`Configuration.Manage` on its own is never enough.

## 8. Uniqueness

### 8.1 What AD enforces

| Attribute | AD-enforced scope | Consequence |
|---|---|---|
| `sAMAccountName` | Domain **[Documented]** | ILM checks forest-wide anyway (§8.2). |
| `userPrincipalName` | Forest, on Windows Server 2012 R2 and later DCs, with documented edge cases in multi-domain forests **[Documented]** | ILM checks it itself too. |
| `mail`, `proxyAddresses`, `employeeID` | None **[Documented]** | ILM is the only check. |

### 8.2 Scope rules

1. **A match in the target forest blocks** the create.
2. **A `mail` or `proxyAddresses` match in a legacy forest is a link candidate, not a block.** During coexistence, the legacy identity is expected to be the same person. An operator confirms the link (§10) or declares a different person, which then blocks.
3. **Any other legacy match blocks.** Revisit this rule if migration waves reuse legacy `sAMAccountName` values for people who haven't moved yet ([V16](open-questions.md#v16)).

### 8.3 How the checks run

- **Target forest:** query a global catalog. Before relying on it, confirm each attribute is in the partial attribute set (`isMemberOfPartialAttributeSet` on the schema object). If an attribute isn't (`employeeID` often isn't, [V8](open-questions.md#v8)), query each domain partition directly.
- **Legacy forests:** there's no cross-forest GC. Query each legacy forest through its own read-only connector.
- **Races inside ILM:** before the directory check, ILM reserves the normalised value in its database behind a unique constraint with a TTL. Two concurrent ILM requests can't both pass.
- **Races across DCs:** every stage of one workflow is pinned to one writable DC, recorded in the saga state. The final uniqueness re-check runs on that DC just before the add. If the DC becomes unavailable mid-saga, the saga pauses rather than silently failing over. Failing over to a DC that hasn't replicated yet can produce a false "unique".

## 9. Account creation (ILM-mastered accounts)

Applies to admin accounts, and to standard users if you fall back to option A. **[Documented]** `unicodePwd` can't be set in the LDAP add, and modifying it needs an encrypted connection (§6). Creation is therefore a sequence of writes. Staging it would be right even without that constraint, because it gives a defined safe failure state:

| # | Stage | Write | If this stage fails |
|---|---|---|---|
| 1 | Reserve | ILM database reservation (§8.3) | Stop. Nothing written. |
| 2 | Re-check | Uniqueness on the pinned DC | Stop. Release the reservation. |
| 3 | Create disabled | LDAP add with `userAccountControl` = 514 (NORMAL_ACCOUNT \| ACCOUNTDISABLE). **[Verify]** whether the DC accepts 514 without a password. If it doesn't, AD's default is 546, which adds PASSWD_NOTREQD, and stage 6 must clear it ([V20](open-questions.md#v20)). | Stop. Nothing to undo. |
| 4 | Set password | Modify `unicodePwd` over the encrypted connection | Account left disabled, with no groups. |
| 5 | Force change | `pwdLastSet` = 0 | Account left disabled, with no groups. |
| 6 | Enable | `userAccountControl` = 512. This clears both ACCOUNTDISABLE and PASSWD_NOTREQD. | Account left disabled, with no groups. |
| 7 | Groups | Add memberships one at a time. Each one is checked against §7. | Account enabled with partial groups. Report partial success, and don't retry blindly. |
| 8 | Verify | Read back from the pinned DC. This includes checking that PASSWD_NOTREQD isn't set. | Raise a discrepancy. |

**The defined failure state is "disabled, no groups".** ILM never deletes automatically. An operator resumes or retires the saga.

For **Okta-mastered standard users**, ILM creates the user through the Okta API and assigns Okta groups. It then verifies that the AD object appears with the expected attributes within an SLA, and raises a discrepancy if it doesn't.

## 10. Identity linking and the person identifier

- **No HR source has been named** for `ImmutablePersonIdentifier` ([V13](open-questions.md#v13)). Until one exists, ILM issues its own opaque identifier.
- **Link evidence is recorded explicitly.** Email is reused on rehire and changes on a name change, so an email-only match is **provisional**. A link becomes **confirmed** with an operator approval (email + human), or with a stronger key once an HR source exists.
- **Containment treats links differently by type.** Confirmed links are contained automatically. Provisional links need confirmation within the containment approval, so ILM never disables the wrong person.

## 11. Scopes, audit and data protection

- **Scopes are stored by OU `objectGUID`.** The DN is resolved when a request is made. Renaming or moving an OU doesn't change its GUID. If a scope's GUID can't be found, the scope becomes inactive, grants fail closed, and ILM alerts.
- **Audit:**
  - Records are append-only. The gMSA's database login can insert audit rows but can't update or delete them.
  - Each record carries the hash of the previous one.
  - Records are forwarded to the SIEM in near real time.
  - A reconciliation job compares the hash-chain head in the database with the SIEM's copy.
- **Database:**
  - It holds personal data plus your privileged topology, so encrypt it at rest.
  - Access is limited to the gMSA and a separate DBA role.
  - Backups are encrypted and included in the threat model.
  - After a restore, run a check that the audit chain continues unbroken.

## 12. Cloud steps and orchestration

**Decision: ILM is the orchestrator and system of record for lifecycle steps.** It calls the Okta API, Microsoft Graph, the Mimecast API, the Citrix API and an Azure Automation runbook directly.

Reasoning:

- **Okta Workflows costs extra beyond Starter.** **[Documented]** The free Workflows Starter org allows 5 active flows and 50 million step executions a month. Larger plans are paid. **[Assumption]** Your entitlement is shown on the Workflows console's Usage page ([V7](open-questions.md#v7)).
- **Power Automate isn't free for this either.** **[Verify]** Its HTTP connector is premium and needs a Premium licence for the flow owner ([V17](open-questions.md#v17)).
- **One timeline.** ILM already exists as the front door, so orchestrating there keeps each leaver on one timeline in one audit log.

Rules:

- **Each step has exactly one owner.** If a Workflows Starter flow is used, it's only for a small, Okta-native, event-triggered job where latency matters, such as an instant Mimecast group update on suspend. That flow reports back to ILM's API, and ILM doesn't also perform the same step.
- **Mailbox conversion to shared** needs Exchange Online PowerShell. ILM starts an Azure Automation runbook. Prefer the Azure Resource Manager API with a certificate credential over a webhook URL. If a webhook is used, treat its URL as a secret.
- **Changes made outside ILM** (for example, a user deactivated directly in the Okta console) are caught by polling the Okta System Log. That needs no internet-facing endpoint. ILM starts its own leavers, so polling latency only affects out-of-band changes.
- **Event hooks are optional and deferred.** If they're adopted later, a small internet-facing receiver validates the hook's secret header and queues the event, and ILM pulls from the queue. **[Documented]** Event hooks are asynchronous, and an org can have at most 25 active hooks. Treat hooks as triggers, not guaranteed delivery. ILM still reconciles against the System Log.
- **Inline hooks aren't used.** They change Okta processes while they run and aren't built for side actions.
- **Calls are idempotent.** Every outbound call carries an idempotency or correlation key. Retries are bounded, and when they run out the step goes to the manual path.

## 13. Solution structure

Start small, and add a project only when its integration is implemented:

```
src/
  Ilm.Domain                    entities, value objects, policies (tier floor, authority rules)
  Ilm.Application               use cases, saga orchestration, approval workflow
  Ilm.Infrastructure.Directory  LDAP adapters (target read/write, legacy read + containment)
  Ilm.Infrastructure.Okta       Okta API adapter (users, sessions, groups, System Log)
  Ilm.Persistence               database, reservations, audit chain
  Ilm.Web                       UI and API, OIDC sign-in
  Ilm.Modules.ReadOnly          read-only views
  Ilm.Modules.Leaver            leaver containment and disposition
tests/
  one test project per src project, plus Ilm.ContainmentScenarios (leaver-containment.md §10)
```

Later projects: Graph, Mimecast, Citrix, Automation, Joiner, Mover, AdminAccounts and Computers. Entra Connect isn't used, so it gets no adapter. Its identities belong in the protected list as a generic category.

## 14. Phasing

| Phase | Scope | Exit criteria |
|---|---|---|
| 1a | OIDC sign-in, read-only views, audit pipeline | Roles resolved server-side. Audit visible in the SIEM. No write rights granted to the gMSA. |
| 1b | Leaver containment ([leaver-containment.md](leaver-containment.md)) | Every scenario in §10 of that document passes in PCATEST. V1 answered. |
| 2 | Joiner, standard users (through the Okta API) | V2, V3 and V4 answered. Uniqueness checks cover both target and legacy. |
| 3 | Mover | OU placement ownership decided. |
| 4 | Admin accounts and computers (direct LDAP path) | Tier 0 floor and attack-path import live. |

The leaver comes before the joiner because it carries the most risk.

## 15. Amendments to spec v2

| Spec v2 item | Change |
|---|---|
| Rule 2 (single writer) | Ownership is per attribute set (§3.2), plus the containment override. |
| Section 5 (fail closed) | Applies to grants only. Containment falls back to the manual strategy ([leaver-containment.md §6](leaver-containment.md#6-failure-direction)). |
| Rule 6 (legacy writes) | New rule 6a: containment-only exception ([leaver-containment.md §7](leaver-containment.md#7-containment-only-legacy-exception-amends-spec-v2-rule-6)). |
| Rule 9 (protected computers) | Adds the Tier 0 servers in §7.4. |
| Rule 12 (Tier 0 membership) | Adds the hard floor (§7.1) and permission-based Tier 0 from attack-path analysis (§7.3). |
| Protected lists and authority rules under `Configuration.Manage` | Need dual control and a versioned approval (§7.5). |
| OIDC group claims | Roles are resolved server-side by group ID or app assignment (§5). |
| `ImmutablePersonIdentifier` | ILM-issued until an HR source exists. Provisional vs confirmed link evidence (§10). |
| Solution structure (~30 projects) | Start with 8 (§13). |
| Database | Encryption at rest, separate DBA role, audit hash chain shipped to the SIEM (§11). |

## Sources

- Microsoft: [Change Windows Active Directory and LDS user password through LDAP](https://learn.microsoft.com/en-us/troubleshoot/windows-server/active-directory/change-windows-active-directory-user-password) (`unicodePwd` can't be added on create; encrypted connection required)
- Microsoft: [Appendix C: Protected Accounts and Groups in Active Directory](https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/security-best-practices/appendix-c--protected-accounts-and-groups-in-active-directory) (AdminSDHolder-protected set, `adminCount`)
- Microsoft: [Search Filter Syntax](https://learn.microsoft.com/en-us/windows/win32/adsi/search-filter-syntax) (`LDAP_MATCHING_RULE_IN_CHAIN`)
- Okta: [Workflows flow limits](https://help.okta.com/wf/en-us/content/topics/workflows/flow-limits.htm) (Starter: 5 active flows, 50M step executions a month)
- Okta: [Event hooks concepts](https://developer.okta.com/docs/concepts/event-hooks/) (asynchronous, 25 active hooks)
