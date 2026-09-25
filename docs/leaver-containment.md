# ILM Portal: leaver containment

**Status:** draft for review. This is phase 1b, the first ILM feature that writes. It amends spec v2 section 5 and rule 6. Tags ([Documented], [Verify], [Assumption]) mean the same as in [architecture.md](architecture.md#how-claims-are-labelled).

The phase can ship only when every scenario in §10 passes in PCATEST.

---

## 1. What "contained" means

A leaver is **contained** when every account confirmed as linked to the person meets all three conditions:

1. Its **authentication authority refuses new sign-ins**, and ILM has observed this.
2. Its **directory account is disabled**, and ILM has observed this.
3. Its **live sessions** in every session-holding system have been revoked. Observed where the system can report it, acknowledged where it can't (§5).

An attempted step doesn't count.

**Residual windows.** Some access survives every step: credentials cached on devices, Kerberos tickets already issued, and access tokens already issued. Each system's residual window is listed in the containment inventory and stated on the case. Their durations are [Verify] for each system ([V12](open-questions.md#v12)).

## 2. Triggers and approval

| Trigger | Source | When containment runs |
|---|---|---|
| Planned leaver | ILM request from IT or the manager | At the effective date and time |
| Urgent termination | ILM request flagged urgent | Immediately after approval |
| Out-of-band | The Okta System Log shows a deactivation or suspension that ILM didn't start, or an AD disable is detected | ILM opens a case to finish and verify containment on the person's other accounts |

**Approval**

- **Every containment needs two people.** The requester and approver must be different. Disabling the wrong person is a denial of service, and it's also an insider-abuse path.
- **Urgent requests** can be approved by the on-call Security Approver. The SLA timer starts at the request, not the approval, so a slow approval shows up in the timings.
- **Provisional links** ([architecture.md §10](architecture.md#10-identity-linking-and-the-person-identifier)) must be confirmed one account at a time as part of the approval. Any provisional link left unconfirmed is shown on the case as *not contained: unconfirmed link* and raises an alert.
- **Legacy forests:** any use of the containment-only exception (§7) needs a Security Approver.
- **Undecided ([D3](open-questions.md#d3)):** whether a Security Approver may raise and execute an urgent termination alone, with a mandatory review within 24 hours.

## 3. Stages

### Stage A: containment (urgent, SLA in minutes)

| Step | Action |
|---|---|
| A0 Resolve | The identity graph lists every linked account: target standard account, admin accounts, legacy accounts and cloud-only accounts. Each account gets a containment owner (§4). ILM takes a snapshot of the pre-containment state (`userAccountControl`, groups, OU, Okta status) for audit and any later reversal. |
| A1 Cut authentication authority | For each account, whatever makes new sign-ins fail: an Okta deactivation for Okta-mastered users, or an AD disable where AD is the authority (§4). |
| A2 Revoke live sessions | **Okta:** clear the user's sessions and revoke OAuth tokens **[Documented]**. **Entra:** Graph `revokeSignInSessions` **[Documented]** invalidates refresh tokens and session cookies. Access tokens already issued stay valid until they expire, unless the app supports Continuous Access Evaluation **[Verify]** ([V15](open-questions.md#v15)). **Citrix:** log off the user's sessions through the Citrix API, because **[Verify]** an AD disable doesn't end existing sessions. **Others:** every other system that holds long-lived sessions is listed in the containment inventory ([V12](open-questions.md#v12)). |
| A3 Disable directory accounts | The accounts not already disabled in A1, each by its containment owner (§4). |
| A4 Verify | See §5. |

Cutting the authority first stops new sessions being created while the old ones are revoked.

### Stage B: disposition (not urgent, hours to days)

Each step has exactly one owner ([architecture.md §12](architecture.md#12-cloud-steps-and-orchestration)). Stage B starts when the case reaches `Contained`, or earlier if a Security Approver starts it manually.

| Step | Action | Owner |
|---|---|---|
| B1 | Mimecast profile group update, replacing the hourly Power Automate poll. If a Mimecast profile controls anything the leaver could still use, move this step into stage A. | ILM, through the Mimecast API ([V19](open-questions.md#v19)) |
| B2 | Record group memberships in the case, then remove them | Okta for Okta-mastered users. ILM for ILM-mastered accounts. |
| B3 | Convert the mailbox to shared, after a retention and hold check | ILM, through an Azure Automation runbook |
| B4 | Remove licences, **after** B3. Removing the licence first can lead to the mailbox being deprovisioned. **[Verify]** When a shared mailbox still needs a licence (size, archive, holds) ([V18](open-questions.md#v18)). | ILM, through Graph and group-based licensing |
| B5 | Move to the leavers OU | The owner of the OU placement set ([architecture.md §3.2](architecture.md#32-attribute-ownership-sets)) |
| B6 | Stamp the case ID on the account | The owner of the profile set |
| B7 | Delete after the retention period | ILM, with a separate approval |

## 4. Containment owner per account

| Account | Authentication authority | A1 / A3 action | Verification | Fallback when verification misses the SLA |
|---|---|---|---|---|
| Target forest, standard user, Okta-mastered | Okta | ILM calls the Okta API to deactivate. Okta push then disables the AD account **[Verify]** ([V1](open-questions.md#v1)). ILM doesn't write AD. | Read the Okta status back. Read `userAccountControl` from a DC until the disable bit is set. | **Containment override:** with Security Approver approval, ILM sets ACCOUNTDISABLE directly and raises a drift incident. It never clears the bit. |
| Target forest, admin account, ILM-mastered | AD | ILM sets ACCOUNTDISABLE on the pinned DC | Read back on the pinned DC, then the replication check (§5) | Retry on the pinned DC, then the manual strategy |
| Legacy forest user imported into Okta (AD-sourced, delegated authentication) | Legacy AD | ILM disables the account over LDAP under the legacy exception (§7) and clears the user's Okta sessions. The Okta status follows the next import **[Verify]** ([V10](open-questions.md#v10)). | Read back on the legacy DC. Sign-in through Okta is refused. | Manual runbook for that forest |
| Legacy forest account not in Okta | Legacy AD | ILM disables the account over LDAP under the legacy exception (§7) | Read back on the legacy DC | Manual runbook for that forest |
| Any object on the Tier 0 floor, or with `adminCount=1` | n/a | **ILM does nothing.** It raises a Tier 0 runbook task for a PAW, with a high-severity alert and an SLA timer. | ILM reads the state back after the manual action | Always manual |

### Suspend or deactivate ([D2](open-questions.md#d2))

- **Deactivate** is expected **[Verify]** to trigger app deprovisioning. That disables the AD account if deactivation is enabled in the AD push settings. Reversing it is disruptive: the user has to be reactivated and reassigned.
- **Suspend** is reversible. It's expected **[Verify]** not to trigger app deprovisioning, which would leave the AD account enabled. The user could then still sign in to Windows and Citrix with their AD password, and ILM would need the override every time.
- **Recommendation:** leavers are deactivated. Suspension-based holds (investigations, garden leave) are out of scope for phase 1b until V1 is answered.

## 5. Verification

| Level | Meaning | Example |
|---|---|---|
| **Observed** | ILM read the resulting state back from the system of record | `userAccountControl` has 0x2 on DC X at time T. The Okta status is `DEPROVISIONED`. |
| **Acknowledged** | The system accepted the request, but the effect can't be read back | Entra `revokeSignInSessions` |
| **Failed** | The request was refused, retries ran out, or the SLA passed | |

- **Contained** means A1 and A3 are Observed for every confirmed-linked account, and A2 is at least Acknowledged for every session holder. Anything less is `PartiallyContained`, which alerts and escalates at the SLA.
- **Replication.** A disable is Observed first on the pinned DC. A follow-up check then reads the account on every writable DC in the domain (one base-scope read each) and records when the last one reports it disabled. The SIEM alerts if any DC still reports the account enabled after the replication SLA. **[Verify]** whether a disable triggers urgent replication in your topology ([V11](open-questions.md#v11)).
- **Post-containment watch.** ILM publishes the contained accounts to a SIEM watchlist. Any successful authentication by one of them within the watch period (14 days proposed) raises a high-severity incident. Sources include the Okta System Log, Entra sign-in logs and DC logon events.
- **SLAs, proposed for the business to set ([Q2](open-questions.md#q2)):** urgent cases contained within 15 minutes of approval; planned cases within 15 minutes of the effective time.

## 6. Failure direction

Spec v2 section 5 ("fail closed") applies to grants only. For containment, failing closed would leave the account live, which is a security fail-open.

| Condition | Grant actions (create, enable, add, change) | Containment actions |
|---|---|---|
| Authority rule missing or overlapping | Fail closed | **ManualControlledStrategy:** a runbook for that account, a high-severity alert, an SLA timer. Never a silent block. |
| Owner system's API unavailable | Fail closed | Bounded retries (proposed: 3 attempts over 5 minutes), then ManualControlledStrategy |
| Target on the Tier 0 floor or `adminCount=1` | Refused | Tier 0 runbook on a PAW. ILM verifies the result afterwards. |
| Verification not reached within the SLA | n/a | Containment override (Okta-mastered users), otherwise ManualControlledStrategy, plus an incident |
| Provisional link unconfirmed | Blocked | Listed on the case as not contained, with an alert |
| ILM itself unavailable | n/a | A manual containment runbook for each forest, kept **outside** ILM. When ILM recovers, it picks up the case through out-of-band detection (§2). |

**ManualControlledStrategy** produces, for each account:

- the system and the immutable identifier (objectGUID, Okta user ID)
- the exact action to take
- the tier of operator allowed to take it
- the verification step

The case stays open until ILM observes the result itself.

## 7. Containment-only legacy exception (amends spec v2 rule 6)

Anyone still in a legacy forest who leaves before their migration wave has neither an approved migration-transition state nor a conclusive target link. Under rule 6 as written, ILM couldn't contain them. Disabling removes access, whereas enabling, creating and changing attributes grant it. Proposed wording:

> **Rule 6a.** ILM may write to a user object in a legacy forest without an approved migration-transition state or a conclusive target link only when all of the following hold:
> 1. The write sets the ACCOUNTDISABLE bit in `userAccountControl` and changes nothing else. Clearing the bit, or writing any other attribute, stays under rule 6.
> 2. The write is part of an approved leaver case whose approval included a Security Approver.
> 3. The object isn't on the Tier 0 floor and doesn't have `adminCount=1`.
> 4. The object's link to the person is confirmed, or was confirmed in the case approval.

### Enforcement

AD permissions can't restrict the direction of a write. A principal that can write `userAccountControl` can clear the disable bit as well as set it. So the rule is enforced in three layers:

1. **Permissions.** The legacy containment identity gets Write Property on `userAccountControl` only, on user objects, in the scoped legacy OUs.
2. **Code.** A dedicated legacy containment writer is the only code path with that identity.
   - It reads the current value and computes `new = old | 0x2`. It refuses if the new value would clear any bit.
   - It sends one LDAP modify that deletes the specific old value and adds the new one. If the attribute changed in the meantime, the modify fails, so no update is lost.
3. **Detection.** A SIEM rule raises a high-severity alert if the legacy containment identity enables an account (event 4722) or changes any attribute other than `userAccountControl`.

**Legacy write identity ([V9](open-questions.md#v9)).** How ILM gets this right depends on the trust topology. It's either the target-forest gMSA, granted access across a forest trust, or a separate gMSA per legacy forest on a Tier 1 host in that forest. Both options grant only the right above.

## 8. Case state machine

```
Requested ──approve──▶ Approved ──start──▶ Containing ──all verified──▶ Contained ──▶ Disposing ──▶ Closed
    │                     │                    │                            ▲
    └──reject/cancel──────┴──▶ Cancelled       ├──SLA passed, gaps──▶ PartiallyContained ──resolved──┤
                                               └──manual fallback──▶ ManualPending ──observed─────────┘
```

- Cancelling is allowed only before `Containing`.
- **Reversal (a mistaken leaver) is a grant.** It's raised as a new request with joiner-level approval. The account is restored from the A0 snapshot, and never automatically.

## 9. Audit record per step

Each step writes one append-only record ([architecture.md §11](architecture.md#11-scopes-audit-and-data-protection)) containing:

- case ID and step ID
- account (immutable identifier) and owner system
- action
- request correlation ID, and the response or error
- verification level and the time it was observed
- DC name, where AD was involved
- requester `sub` and approver `sub`
- whether the containment override or the legacy exception was used
- pre-containment snapshot reference

## 10. Acceptance scenarios

Run these in PCATEST, codified in `Ilm.ContainmentScenarios`. Phase 1b doesn't ship until all of them pass.

1. **Okta-mastered, deactivate.** An Okta-mastered user is deactivated through ILM. The AD account is disabled within the SLA, with no ILM write to AD. Record the observed delay.
2. **Okta-mastered, suspend (evidence for V1 and D2).** A user is suspended directly in Okta. Record whether the AD account changes. The expected result is that it doesn't.
3. **Override.** Deactivation in Okta doesn't disable the AD account within the SLA (simulated by turning off AD deprovisioning in PCATEST). With approval, the containment override disables the account and raises a drift incident.
4. **Legacy account, no target link.** The rule 6a path disables the account. An attempt through the same writer to change any other attribute, or to clear the bit, is refused in code, and a forced enable raises the SIEM alert.
5. **Missing authority rule.** The ManualControlledStrategy runbook is produced, a high-severity alert fires, and the SLA timer starts. The case doesn't sit silently blocked.
6. **Tier 0.** A leaver with an admin account where `adminCount=1` gets no ILM write to that account. A Tier 0 task is raised. After the manual disable, ILM marks the account Observed.
7. **Okta API unavailable.** Retries run out, then the manual strategy takes over.
8. **Provisional link.** A rehire whose email address was reused: the previous holder's account isn't contained unless the approver confirms the link.
9. **Citrix.** A user with an active Citrix session is contained, and the session list for the user is empty afterwards (Observed).
10. **Replication.** The follow-up check records a disable on every DC in the domain, and the SIEM alert fires when one DC is made unreachable during the test.
11. **Protection change.** Removing an object from a protected list without a Security Approver is refused. Removing a hard-floor identifier is refused even with one.
12. **Out-of-band.** A user deactivated directly in the Okta console is picked up from the System Log, and ILM opens a case covering the person's other accounts.
