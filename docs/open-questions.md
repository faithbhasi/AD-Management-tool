# ILM Portal: open questions

These are the decisions and checks the design depends on. Each one names the phase it blocks ([architecture.md §14](architecture.md#14-phasing)). Close an item by recording the answer and the evidence here, then update the document that refers to it.

## Decisions needed

| ID | Decision | Recommendation | Depends on | Blocks |
|---|---|---|---|---|
| <a id="d1"></a>D1 | Source of authority for standard users: option B (Okta-mastered, ILM creates through the Okta API) or option A (AD-mastered, Okta imports) | **B**, if the Okta → AD push pilot reaches production. **A**, if joiners stay manual with no HR feed. | Pilot outcome, V1–V5 | Phase 2 |
| <a id="d2"></a>D2 | Suspend or deactivate an Okta-mastered leaver | **Deactivate.** Holds based on suspension stay out of scope until V1 is answered. | V1 | Phase 1b |
| <a id="d3"></a>D3 | Whether a Security Approver may raise and execute an urgent termination alone, with a mandatory review within 24 hours | Allow it, alert on every use, and report on it monthly | Security policy | Phase 1b |

## Checks

| ID | What to establish | How | Owner | Blocks |
|---|---|---|---|---|
| <a id="v1"></a>V1 | What an Okta deactivation and an Okta suspension each do to the pushed AD account, and how long they take | PCATEST, leaver-containment §10 scenarios 1–2 | Okta engineer | 1b |
| <a id="v2"></a>V2 | The full Okta → AD push attribute mapping. This list becomes the profile set that Okta owns. | Export the push mappings | Okta engineer | 2 |
| <a id="v3"></a>V3 | The Okta AD import matching rules, and whether an account ILM creates could be matched to, or duplicated against, an Okta-sourced profile | Review the import settings, then test in PCATEST | Okta engineer | 2 |
| <a id="v4"></a>V4 | Whether Okta moves pushed users between OUs when their department changes, or only places them at creation | PCATEST | Okta engineer | 3 |
| <a id="v5"></a>V5 | Which system owns the password for Okta-mastered users (password push, delegated authentication or neither), and so which system owns resets | Review the Okta AD agent and app settings | Okta engineer | 2 |
| <a id="v6"></a>V6 | Org or custom authorization server for ILM sign-in, and whether your licence includes a custom one | Check the Okta licence, then decide | Architect | 1a |
| <a id="v7"></a>V7 | Your Workflows entitlement, and whether helper flows count toward the Starter limit of 5 active flows | Workflows console → Usage page; Okta's "Parent flows and other flow types" page | Okta engineer | Only if Workflows is used |
| <a id="v8"></a>V8 | Whether `mail`, `proxyAddresses`, `userPrincipalName`, `sAMAccountName` and `employeeID` are in the global catalog partial attribute set in the target forest and each legacy forest | Check `isMemberOfPartialAttributeSet` on each attribute's schema object | AD engineer | 2 |
| <a id="v9"></a>V9 | The trust topology between the target forest and each legacy forest, and so which identity performs rule 6a writes (target gMSA across a trust, or one gMSA per forest) | Trust inventory | AD engineer | 1b |
| <a id="v10"></a>V10 | For users imported into Okta from a legacy forest: what the next import does when the AD account is disabled, how often imports run, and whether delegated authentication refuses a disabled account immediately | PCATEST, or a legacy pilot OU | Okta engineer | 1b |
| <a id="v11"></a>V11 | Whether disabling an account triggers urgent replication, and the replication SLA for each site | Replication topology review, then a test | AD engineer | 1b |
| <a id="v12"></a>V12 | The containment inventory: every system that holds long-lived sessions, how each one is revoked, and each one's residual window. Known entries: **Citrix** (confirm an AD disable doesn't end sessions; which product and API); **Entra** (access token lifetime and Continuous Access Evaluation support); **Kerberos** (ticket lifetime); **VPN**, if used. | Workshop with platform owners, then tests | Security architect | 1b |
| <a id="v13"></a>V13 | A source for `ImmutablePersonIdentifier` (an HR system). ILM issues its own identifier until one exists. | Ask HR / HRIS | Architect | None yet |
| <a id="v14"></a>V14 | The break-glass design: vault, custody, dual-control retrieval, alerting and rotation after use | Security design | Security architect | 1a go-live |
| <a id="v15"></a>V15 | Whether Microsoft 365 is federated to Okta, and which Graph application permission `revokeSignInSessions` needs in your tenant | Tenant review | M365 engineer | 1b |
| <a id="v16"></a>V16 | Whether migration waves reuse legacy `sAMAccountName` values for people who haven't moved yet. If they do, "any other legacy match blocks" (architecture §8.2) needs refining. | Migration team | Architect | 2 |
| <a id="v17"></a>V17 | Whether the Power Automate HTTP connector needs a Premium licence for the flow owner | Microsoft licensing | M365 engineer | Only if Power Automate is kept |
| <a id="v18"></a>V18 | When a shared mailbox still needs a licence (size, archive, litigation hold) | Microsoft documentation, then the tenant's hold policy | M365 engineer | Stage B |
| <a id="v19"></a>V19 | Whether the Mimecast API can change profile group membership, and how it authenticates | Mimecast documentation | M365 engineer | Stage B |
| <a id="v20"></a>V20 | Whether a DC accepts an LDAP add with `userAccountControl` = 514 and no password, or applies 546 (PASSWD_NOTREQD). This fixes the stage 3 and stage 6 values in architecture §9. | PCATEST | AD engineer | 4 |

## Other open items

| ID | Item | Blocks |
|---|---|---|
| <a id="q1"></a>Q1 | **Review spec v2 from section 7 ("protected-object") onward.** It covers the computer lifecycle, the approval and audit data model, and the integration detail. Both pastes so far were cut off, so commit the spec to the repo as `docs/spec-v2.md` instead of pasting it. | All phases |
| <a id="q2"></a>Q2 | **Business SLAs.** Urgent and planned containment times (15 minutes each proposed), the replication SLA, and the post-containment watch period (14 days proposed). | 1b |
