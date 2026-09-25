# Operations

Day-to-day running of ILM: what to watch, what the workers do, and the routine procedures for each role.

## Signals

| Signal | Where | Healthy | Act when |
|---|---|---|---|
| Liveness | `GET /health/live` | 200 | Anything else. Restart the app pool and check the event log. |
| Readiness | `GET /health/ready` (status and per-check state only, no details) | `database: Healthy` | `database` is Unhealthy (see [TROUBLESHOOTING.md](TROUBLESHOOTING.md)) |
| Connector detail | Admin → Health | Every configured directory, Okta and audit forwarding `ok` | A directory or Okta connector is degraded. Session systems shown as *not integrated* are expected: they're manual tasks by design. |
| Audit forwarding lag | Admin → Health, High `AuditForwardingLag` alert | ≤ `MaxForwardingLag` (1000) | Any lag alert. The sink or network is down. |
| Alerts | Dashboard → Alerts | None unacknowledged | Critical at once, High within the SLA |
| Manual tasks | Dashboard, Tasks | None past SLA | `ManualTaskSlaBreached` (Critical) |
| Unresolved containment | Dashboard "Active containment" | Empty, or within SLA | Anything in `ManualContainmentRequired`, `PartiallyContained` or `ReconciliationRequired` |
| Reconciliation | Reconciliation page | "InSync" | `ContainedAccountReEnabled` (Critical) or `OutOfBandChange` (High) |
| Logs | JSON console to stdout. Under IIS use ANCM stdout, or better, OTLP. | No `Error` from workers | `Background worker … failed` repeating |
| Telemetry | OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set: traces for ASP.NET Core, HttpClient and ILM activities; metrics for worker runs, audit forwarding and leaver transitions | — | — |

`/health/ready` is anonymous and deliberately says nothing beyond the status of each check (`Health_endpoints_expose_no_details`).

## Background worker

`IlmBackgroundWorker` ticks every 5 s and runs:

| Job | Default interval | Does |
|---|---|---|
| Audit forwarding | 15 s | Forwards sealed records to each sink after its checkpoint, and alerts on lag |
| Leaver maintenance | 30 s | Expires stale approvals, starts scheduled containment when due, re-verifies in-flight requests, advances post-containment stages, and marks SLA breaches |
| Reconciliation | 60 min | Rereads every known identity, records drift, detects re-enabled contained accounts and out-of-band Okta changes |

A failed job is logged and counted (`ilm.worker.runs`, outcome `error`), and runs again on the next interval. Run workers on one node only (see [DEPLOYMENT.md](DEPLOYMENT.md)).

## Routine procedures

### Lifecycle Operator: raise and run a leaver

1. **People** → search → open the person. Check their linked accounts, the owner of each account's `AccountEnabledState`, protection status, and any unresolved links.
2. **Leavers → New**. Pick the person, urgency (Urgent now, or Planned with an effective time), a ticket reference and a reason. Review the plan preview, which gives the method for every account and explains any manual steps.
3. Ask a Lifecycle Approver to approve it, or a Security Approver if the plan includes legacy containment or any Tier 0 or Unknown object.
4. **Start containment**. Planned requests start automatically when due.
5. Work any **manual tasks**: follow the runbook, then record the change ticket as evidence. ILM rereads directory and Okta targets before it closes a task.
6. The request reaches **SafelyContained**. The non-urgent tasks (retention, ownership, Mimecast and Citrix reconciliation) follow.

### Lifecycle Approver or Security Approver

- **Approvals** → open the item. Check the plan, the targets and the reason, then approve or reject. ILM re-checks your roles with Okta when you do.
- **Identity link confirmation** tasks: find out whether the account belongs to the person, then approve or reject the link. You can't approve a link you proposed, or one backed only by name evidence.
- **Security Approver only**:
  - configuration versions
  - feasibility reports
  - issuer migrations
  - rollback (re-enable) requests
  - Tier 0 manual tasks. The work is done by a Tier 0 administrator on a PAW; the Security Approver records the result.

### Configuration Administrator

1. **Admin → Configuration** → export the active version, edit it, then propose it with a summary and change reference. Validation runs at once, and any issues are listed with their codes.
2. A Security Approver (a different person) approves it.
3. Activate it. The previous version becomes the rollback target. To roll back, propose a copy of an older version; it takes the same approval path.
4. Attack-path imports: **Admin → Protection → Import** (see [PROTECTED-OBJECTS.md](PROTECTED-OBJECTS.md)).

### Auditor

- **Audit**: filter by action, operator, target, operation or time. **Verify** checks the chain against the database and the off-box copy. **Export** produces a CSV with formula neutralisation, and the export itself is audited.
- Monthly: run `verify-audit` on the host and record the result. Compare the SIEM record count with the database sequence.

### Monthly checks

| Check | Owner |
|---|---|
| Restore test with audit verification ([DATABASE-SECURITY.md](DATABASE-SECURITY.md)) | DBA with an Auditor |
| Review the ILM Okta role group memberships and the System Log for changes to those groups | Okta engineer and Security Approver |
| Review `ILM-Portal-Hosts` membership, and the gMSA's delegation ACEs against `Grant-IlmDelegation.ps1` | AD engineer |
| Import a fresh attack-path analysis | Security architect and Configuration Administrator |
| Review alerts and SLA breaches for trends | Service owner |
| Patch the host (Windows, the .NET runtime and Hosting Bundle) | Platform team, under [CHANGE-MANAGEMENT.md](CHANGE-MANAGEMENT.md) |

## CLI verbs

Run these on the host (`dotnet Ilm.Web.dll <verb>`). Each exits non-zero on failure.

| Verb | Use |
|---|---|
| `migrate` | Apply database migrations with the migration identity (change window only) |
| `bootstrap-config --file <json> --change <ticket>` | Create configuration version 1. Only works while no configuration exists. |
| `check-config --file <json>` | Validate a configuration document offline. Exit 4 if there are issues. |
| `verify-audit` | Verify the audit chain and the forwarded copy. Exit 3 if broken. |
| `feasibility --mode mock\|pcatest [--options <json>] [--output <file>]` | Run the Okta-to-AD feasibility harness |
| `seed-dev`, `export-dev-config` | Development only |
