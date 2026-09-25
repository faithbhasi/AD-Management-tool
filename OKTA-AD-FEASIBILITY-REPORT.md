# Okta-to-AD provisioning feasibility report

> **Mock run.** This report was generated against the in-process mock Okta org and mock directory.
> It demonstrates the harness and report format only. It is **not** evidence about PCATEST or production
> behaviour, and it can never support enabling Okta provisioning.

| Item | Value |
|---|---|
| Run ID | `7c7f97cd-a73f-458a-8a13-425c21784684` |
| Environment | Mock (in-process, not PCATEST) |
| Tested Okta org | https://okta.example.test (mock) |
| Tested AD integration | Mock Okta AD Agent → corp.example.test |
| Assignment mechanism | Group `00gAdPushTarget00001` |
| Target connector | corp |
| Started / completed (UTC) | 2026-09-25 12:02:54Z / 2026-09-25 12:02:55Z |
| **Recommendation** | **No-Go** |

**Rationale:** Mock evidence only. A recommendation other than No-Go requires a PCATEST run.

## Source and profile settings

Mock: Okta is the profile source for pushed attributes (simulated).

## Push mappings

Mock: login→userPrincipalName, email→mail and proxyAddresses, department→department, managerId→manager (simulated).

## Target OU behaviour

#5 Verify target OU selection: **Passed**. Placed in OU=Finance,OU=Staff,OU=ILM Managed,DC=corp,DC=example,DC=test.

## Create result

- #1 Create a staged Okta test user through the API: **Passed**. Created 00umfHdJgJn8sFxhOKfF in STAGED state.
- #2 Assign the user to the group or application that drives AD provisioning: **Passed**. Assigned through Group 00gAdPushTarget00001.
- #3 Verify whether an AD object is created: **Passed**. AD object 4ab75484-8350-4750-b9f3-f30a41c28602 created (1 match). Provisioning required the Okta user to be activated first.
- #6 Verify sAMAccountName: **Observed**. sAMAccountName 'ilmfeas-f068ab7d'.
- #7 Verify UPN: **Passed**. UPN 'ilmfeas-f068ab7d@example.test' (Okta login 'ilmfeas-f068ab7d@example.test').
- #8 Verify mail and proxy addresses: **Passed**. mail 'ilmfeas-f068ab7d@example.test'; proxyAddresses [SMTP:ilmfeas-f068ab7d@example.test].
- #9 Verify department: **Passed**. department 'Finance'.
- #10 Verify manager: **Skipped**. No manager supplied (ManagerOktaId).
- #11 Verify account enabled state: **Observed**. AD account enabled: True.
- #12 Verify initial password behaviour: **Observed**. ILM cannot and does not read passwords. Operator notes: Mock: no password is pushed; initial password behaviour must be observed in PCATEST.
- #13 Verify group memberships: **Observed**. 1 group(s): S-1-5-21-1000000001-1000000002-1000000003-513.

## Update result

- #20 Verify whether an ILM AD change is overwritten by the next Okta push: **Observed**. Overwritten: 'department' reverted to 'Finance' after the next push.

## Suspension result

- #16 Verify suspension behaviour: **Observed**. Suspension did not change the AD account; containment must not rely on suspension alone.

## Deactivation result

- #17 Verify deactivation behaviour: **Passed**. Okta user is DEPROVISIONED.
- #18 Verify whether deactivation disables AD: **Passed**. Deactivation disabled the AD account.

## Reactivation result

- #19 Verify whether reactivation re-enables AD: **Observed**. Reactivation re-enabled the AD account.
- #25 Verify that factors and application assignments remain intact: **Passed**. Factors (0) and application assignments (0) unchanged by the lifecycle cycle.

## Session result

Okta session revocation accepted (HTTP 204). Downstream application sessions are not proven closed.

## Duplicate result

- #14 Verify duplicate-user behaviour: **Passed**. Duplicate create rejected (HTTP 400 E0000001); one user remains.
- #15 Verify existing-AD-user matching behaviour: **Skipped**. No pre-existing synthetic AD user was supplied (ExistingAdUserLogin).
- #23 Verify retry and duplicate prevention: **Passed**. Concurrent retries produced exactly one Okta user.
- #24 Verify that an existing Okta user is linked rather than duplicated: **Passed**. Search by email finds the existing user, so ILM links instead of creating.

## Timing observations

- #4 Record the provisioning delay: **Observed**. Observed delay 1.4 s over 4 poll(s). No expected duration is assumed.
- No expected provisioning duration is assumed by ILM; workflows poll and time out with an alert.

## Failure behaviour

- #22 Verify failure when the Okta AD Agent is unavailable: **Passed**. No AD object during the outage; 1 non-success System Log event(s) recorded.
- #21 Verify Okta API and System Log correlation: **Passed**. 13 event(s) correlated to 00umfHdJgJn8sFxhOKfF: application.provision.user.deactivate, application.provision.user.push, application.provision.user.push_profile, application.provision.user.reactivate, group.user_membership.add, user.account.update_profile, user.lifecycle.activate, user.lifecycle.create, user.lifecycle.deactivate, user.lifecycle.suspend, user.lifecycle.unsuspend, user.session.clear+oauth2.tokens.revoke.

## Licensing dependencies discovered

Mock: none. Licensing dependencies (for example Okta Workflows or Lifecycle Management) must be confirmed for PCATEST.

## Unsupported assumptions

- This run used the in-process mock Okta org and mock directory. It is not evidence about PCATEST behaviour.
- Okta profile-source and push-mapping settings are recorded from operator observation; the harness cannot read all of them through supported APIs.
- Initial password behaviour is not observable by ILM, which never reads passwords.

## Conditions for activation

- Okta push overwrites ILM changes to 'department': ILM must never write Okta-mapped attributes for Okta-mastered users.
- Reactivation in Okta re-enables AD: reactivation must be treated as an access grant with approval.
- During an Okta AD Agent outage no AD object is created; ILM must keep the request pending and alert rather than report success.

## Clean-up

- #26 Verify rollback and clean-up of synthetic test users: **Passed**. Deactivated and deleted 3 synthetic Okta user(s).
- AD object 4ab75484-8350-4750-b9f3-f30a41c28602 (CN=Synthetic Testerf068ab7d,OU=Finance,OU=Staff,OU=ILM Managed,DC=corp,DC=example,DC=test): ILM never deletes directory objects; remove it through the lab clean-up procedure.

## All checks

| # | Check | Outcome | Observation |
|---|---|---|---|
| 1 | Create a staged Okta test user through the API | Passed | Created 00umfHdJgJn8sFxhOKfF in STAGED state. |
| 2 | Assign the user to the group or application that drives AD provisioning | Passed | Assigned through Group 00gAdPushTarget00001. |
| 3 | Verify whether an AD object is created | Passed | AD object 4ab75484-8350-4750-b9f3-f30a41c28602 created (1 match). Provisioning required the Okta user to be activated first. |
| 4 | Record the provisioning delay | Observed | Observed delay 1.4 s over 4 poll(s). No expected duration is assumed. |
| 5 | Verify target OU selection | Passed | Placed in OU=Finance,OU=Staff,OU=ILM Managed,DC=corp,DC=example,DC=test. |
| 6 | Verify sAMAccountName | Observed | sAMAccountName 'ilmfeas-f068ab7d'. |
| 7 | Verify UPN | Passed | UPN 'ilmfeas-f068ab7d@example.test' (Okta login 'ilmfeas-f068ab7d@example.test'). |
| 8 | Verify mail and proxy addresses | Passed | mail 'ilmfeas-f068ab7d@example.test'; proxyAddresses [SMTP:ilmfeas-f068ab7d@example.test]. |
| 9 | Verify department | Passed | department 'Finance'. |
| 10 | Verify manager | Skipped | No manager supplied (ManagerOktaId). |
| 11 | Verify account enabled state | Observed | AD account enabled: True. |
| 12 | Verify initial password behaviour | Observed | ILM cannot and does not read passwords. Operator notes: Mock: no password is pushed; initial password behaviour must be observed in PCATEST. |
| 13 | Verify group memberships | Observed | 1 group(s): S-1-5-21-1000000001-1000000002-1000000003-513. |
| 14 | Verify duplicate-user behaviour | Passed | Duplicate create rejected (HTTP 400 E0000001); one user remains. |
| 15 | Verify existing-AD-user matching behaviour | Skipped | No pre-existing synthetic AD user was supplied (ExistingAdUserLogin). |
| 16 | Verify suspension behaviour | Observed | Suspension did not change the AD account; containment must not rely on suspension alone. |
| 17 | Verify deactivation behaviour | Passed | Okta user is DEPROVISIONED. |
| 18 | Verify whether deactivation disables AD | Passed | Deactivation disabled the AD account. |
| 19 | Verify whether reactivation re-enables AD | Observed | Reactivation re-enabled the AD account. |
| 20 | Verify whether an ILM AD change is overwritten by the next Okta push | Observed | Overwritten: 'department' reverted to 'Finance' after the next push. |
| 21 | Verify Okta API and System Log correlation | Passed | 13 event(s) correlated to 00umfHdJgJn8sFxhOKfF: application.provision.user.deactivate, application.provision.user.push, application.provision.user.push_profile, application.provision.user.reactivate, group.user_membership.add, user.account.update_profile, user.lifecycle.activate, user.lifecycle.create, user.lifecycle.deactivate, user.lifecycle.suspend, user.lifecycle.unsuspend, user.session.clear+oauth2.tokens.revoke. |
| 22 | Verify failure when the Okta AD Agent is unavailable | Passed | No AD object during the outage; 1 non-success System Log event(s) recorded. |
| 23 | Verify retry and duplicate prevention | Passed | Concurrent retries produced exactly one Okta user. |
| 24 | Verify that an existing Okta user is linked rather than duplicated | Passed | Search by email finds the existing user, so ILM links instead of creating. |
| 25 | Verify that factors and application assignments remain intact | Passed | Factors (0) and application assignments (0) unchanged by the lifecycle cycle. |
| 26 | Verify rollback and clean-up of synthetic test users | Passed | Deactivated and deleted 3 synthetic Okta user(s). |

## Final recommendation

**No-Go.** Mock evidence only. A recommendation other than No-Go requires a PCATEST run.

OktaApiProvisioningStrategy stays disabled until a Security Approver approves a PCATEST report with a Go or Conditional Go
recommendation and an approved configuration version references it by run ID and report hash.
