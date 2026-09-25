# Troubleshooting

Every entry gives the symptom, the likely cause, and what to check. Errors shown to users are **safe categories** (`SafeErrorCategory`), never stack traces. The details are in the JSON log and the audit record.

## Startup

| Symptom | Cause | Fix |
|---|---|---|
| `ILM refused to start: … SSL Mode=VerifyFull …` | The production connection string isn't TLS with full verification | Add `SSL Mode=VerifyFull`, and trust the PostgreSQL server's CA on the host |
| `… contains a password …` or `… holds a secret value …` | A password or secret was put in `appsettings` or an environment connection string | Remove it. Use Kerberos (`gss`) for the database, and environment variables *named by* `…EnvironmentVariable` settings for secrets. |
| `… mock OIDC provider is development-only …` or `… Okta Live mode …` | Development settings reached production | Check `ASPNETCORE_ENVIRONMENT` and remove the `appsettings.Development.json` values |
| `… computed redirect URI must appear exactly in AllowedRedirectUris` | `PublicBaseUrl` + `/signin-oidc` isn't listed exactly (scheme, host, port, path and case all count) | Add the exact URI, and register the same one in the Okta app |
| `No audit HMAC key: set ILM_AUDIT_HMAC_KEY …` | The key wasn't injected | Inject it from the vault into the app pool environment (base64, at least 32 bytes) |
| `The audit HMAC key must be at least 256 bits` | The key is too short | Generate 32 random bytes and base64-encode them |
| IIS 500.30 / 500.31 | The Hosting Bundle is missing, or the app threw at startup | Install the .NET 10 Hosting Bundle. Temporarily enable `stdoutLogEnabled` in `web.config` to see the refusal message, then turn it off again. |

## Sign-in

| Symptom | Cause | Fix |
|---|---|---|
| Redirected to **Sign-in failed** | Issuer, audience or signature validation failed, or the state or correlation cookie was lost | Check `Ilm:Authentication:Issuer` matches the token's `iss` **exactly** (org and custom authorisation servers differ), check `ClientId`, and check the clock skew on the host |
| Signed in, but "You have no roles" on **Me** with *Role verification failed* | The Okta API lookup failed, or the configuration is unavailable | Admin → Health → `okta:management-api`. Check the service app's scopes and admin role, its key thumbprint, and outbound 443. ILM fails safe: no roles. |
| **Me** says the identity uses a new issuer and holds no roles | The issuer changed, and this is now a pending issuer migration | Propose the migration (old → new user) and have a Security Approver approve and apply it ([OKTA-OIDC.md](OKTA-OIDC.md)) |
| A role is missing after an Okta group change | Roles are cached for up to `RoleResolution.CacheSeconds` (≤ 300 s) | Wait, or sign out and in again. Privileged actions always re-check without the cache. |
| A user holds a group *named* like an ILM group but gets no role | By design: only the immutable group IDs in `RoleMappings` count | Add the group **ID** in a configuration change, if intended |

## Leavers

| Symptom | Cause | Fix |
|---|---|---|
| "The plan changed after approval; a new approval has been requested" | A link, protection state, authority rule or account changed between approval and execution | Review the new plan and approve it again |
| "The approval expired" | More than `LeaverApprovalValidityHours` passed before execution | Approve again |
| Stuck in `ManualContainmentRequired` after the task was recorded | ILM rereads the directory or Okta and hasn't seen the contained state yet (replication, Okta push delay, or wrong account) | Check the account on the **same DC** shown in the action. Re-verify from the leaver page. |
| `ContainmentVerificationPending` for an Okta-mastered user | Waiting for the Okta AD agent to disable the pushed AD account | Wait up to `DirectoryVerificationTimeoutSeconds` (300). After that ILM creates a manual task automatically. Check the Okta AD agent. |
| "Tier 0 containment tasks are recorded by a Security Approver" | The target is protected or its status is Unknown | A Tier 0 administrator performs the runbook from a PAW, and a Security Approver records it |
| Protection shows **Unknown** | Membership or attributes couldn't be read, a forest was unreachable for the FSP check, or the platform identity list is incomplete | Check the connector health for every forest, and set `Ilm:Platform` completely, including `ManagedPasswordRetrieversVerified` |
| "Containment for this person is already running" | Another execution holds the per-person lease | Wait for it to finish (the lease expires after 10 minutes if a node died) |
| An identity link confirmation task won't go away | The link is still Provisional or Ambiguous, or the account has no link record | Approve (by a second person) or reject the link on the person's page |

## Directory

| Symptom | Cause | Fix |
|---|---|---|
| Connector `Unavailable (LdapException)` | DNS, firewall (636/389/88), certificate or time skew | [ACTIVE-DIRECTORY.md](ACTIVE-DIRECTORY.md#network). Test with `Test-NetConnection dc01.corp.example.test -Port 636`, `nltest /dsgetdc:corp.example.test` and `w32tm /query /status`. |
| LDAPS bind fails but 389 works | The DC certificate doesn't chain, the name doesn't match, or the CRL is unreachable | Import the forest CA root, use FQDNs in `DomainControllers`, and open CRL and OCSP |
| `InsufficientDirectoryRights` on disable | The delegation is missing, or the object has inheritance disabled (`adminCount`) | Re-run `Grant-IlmDelegation.ps1` for that OU. If the object is protected, ILM should not write to it anyway. |
| `ConcurrencyConflict` repeated | Another process keeps changing `userAccountControl` | Find the other writer (Security log 4738), because only one writer may own the set |
| "No writable DC" | Every configured DC is an RODC, unreachable, or in the wrong domain or forest | Fix `DomainControllers` in a configuration change |

## Audit

| Symptom | Cause | Fix |
|---|---|---|
| `verify-audit` reports "content does not match its hash" | A record was modified after sealing. Or you're running code older than the fix that truncates timestamps to microseconds before hashing. | Treat it as a security incident unless you're on older code. Compare with the SIEM copy. |
| "MAC invalid" / "unknown key" | Records edited and re-hashed without the key, or a retired key is missing from the host | Security incident, or restore the retired key for verification |
| "present in the off-box sink but missing from the database" | Tail truncation, or a restore from an older backup | Expected after a planned restore (see [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md)). Otherwise, a security incident. |
| `AuditForwardingLag` alert | The share or SIEM is unreachable, or the token has expired | Check the path permissions for the gMSA and the SIEM endpoint. Forwarding resumes from the checkpoint automatically. |

## Development

| Symptom | Fix |
|---|---|
| Browser warns about the certificate | `dotnet dev-certs https --trust`. The smoke test disables verification only for `localhost`. |
| Want a clean slate | Stop the app and delete `src/Ilm.Web/App_Data`. The next start migrates and seeds again. |
| `docker compose` says `run scripts/dev-env.sh first` | Generate `.env` with random development passwords: `scripts/dev-env.sh` |
| PostgreSQL integration test is skipped | Set `ILM_TEST_POSTGRES` to an admin connection string for a disposable server |
