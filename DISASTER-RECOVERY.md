# Disaster recovery

ILM removes access. If it's down, leavers must still be contained, by hand, using the same runbook. ILM holds no state that the directories or Okta depend on, so losing ILM never grants or removes access by itself.

## Objectives (proposed; confirm with the service owner)

| Item | Target |
|---|---|
| RTO (portal usable again) | 4 hours |
| RPO (database) | 15 minutes, with continuous WAL archiving |
| Audit RPO | 0 for records that reached the off-box sink. Records not yet forwarded are covered by the database RPO. |
| Manual containment while ILM is down | Within the urgent SLA (15 minutes), using [MANUAL-CONTAINMENT.md](MANUAL-CONTAINMENT.md#when-ilm-itself-is-unavailable) |

## Scenarios

### ILM host lost

1. Build a new Windows host. Join it to the domain and add it to `ILM-Portal-Hosts` (a Tier 0 change). Reboot, then `Install-ADServiceAccount svc-ilm` and `Test-ADServiceAccount`.
2. Deploy the same release ([DEPLOYMENT.md](DEPLOYMENT.md)) and re-inject the secrets from the vault.
3. Update `Ilm:Platform:HostComputerSids` and `HostDnsNames`, and remove the old host from the group and from DNS.
4. Check `/health/ready` and `verify-audit`.

No data is lost. The database and the sink are elsewhere.

### Database lost or corrupted

1. Stop the ILM app pools, so nothing writes to a half-restored database.
2. Restore the latest base backup, plus WAL, to the recovery point on a clean server. Backups are encrypted, and the keys are in the vault under dual custody.
3. Run `dotnet Ilm.Web.dll verify-audit` against the restored database, with the off-box sink available.
   - **Expected:** either valid, or "present in the off-box sink but missing from the database" for the records written after the recovery point. Record the gap in sequence numbers in the incident.
   - **Anything else** (hash or MAC failures inside the restored range) means the backup itself was tampered with. Stop and escalate as a security incident.
4. **Reconcile what happened after the recovery point.** The sink holds the audit records for it:
   - every leaver that changed state after the recovery point
   - every configuration activation
   - every approval
5. For each affected leaver, open it in ILM and **re-verify**. Containment already done in the directory or Okta is observed and marked `AlreadyInDesiredState`, with no duplicate writes. Anything missing becomes a manual task.
6. If a configuration activation was lost, re-propose it from the sink record's content hash and the source-controlled document. It needs approval again.
7. Start the app pools. Check readiness. Run reconciliation from the Reconciliation page.

The audit chain continues from the restored tail, and the sink keeps the lost records. Verification then reports the gap permanently. That's the intended evidence that a restore happened. Keep the incident reference alongside it.

### HMAC key lost

- New records can't be sealed, and ILM refuses to start without a key. Generate a **new key with a new key ID** and inject it.
- Old records can't be MAC-verified without the old key. They're still hash-chained and match the sink copy, so verification reports "unknown key" for them. Record this in the incident. Don't reuse the old key ID.
- Vault the key with dual custody. Losing it is avoidable.

### HMAC key disclosed

- Rotate: set a new key ID and key. The old key stays available for verification only.
- An attacker with the old key and database access could forge a consistent chain for past records. The **off-box sink copy** still shows any divergence, so run `verify-audit` with the sink straight away and compare with the SIEM (residual risk R5).

### Okta unavailable

- Operators can't sign in, because ILM has no local accounts by design. Run leavers manually ([MANUAL-CONTAINMENT.md](MANUAL-CONTAINMENT.md)), using AD tools from PAWs and the Okta console once it's back.
- If Okta is up but the management API is failing, containment of Okta-mastered users falls back to manual tasks with alerts. The request stays visible.

### A directory forest unavailable

Containment for that forest's accounts falls back to `FailedBeforeChange` and then a manual task. Protection checks that need the forest (FSP lookups) return Unknown, which denies automation. Nothing is silently skipped.

### Break-glass access to ILM data

There's no application break-glass login. If you need to read ILM data while Okta is down, a DBA runs read-only queries as `ilm_auditor_ro` under an incident ticket, with the access logged. They never write, and they never touch the history tables.

## Test schedule

| Test | Frequency |
|---|---|
| Restore to an isolated host, plus audit verification against the sink | Monthly |
| Host rebuild from scratch in the lab | Every 6 months |
| Tabletop of manual containment with ILM down | Every 6 months |
| HMAC key rotation drill | Yearly |
