# ILM Portal (Identity Lifecycle Manager)

ILM Portal is a Tier 1 administrative web application for identity lifecycle work across Okta, a target Active Directory forest and legacy AD forests. It separates people from their accounts, decides source of authority per attribute set, refuses to touch Tier 0, and contains leavers across every system they have access to. It never lets a containment request fail silently.

Everything in this repository uses **fictional data** (`example.test` names, synthetic IDs). There are no real domains, users, OUs, credentials or tokens.

## Status

| Capability | State |
|---|---|
| Okta OIDC sign-in (in-process mock in Development), roles resolved on the server from immutable IDs, OU scopes | Built and tested (mock OIDC) |
| OU browsing, user, computer and person views, identity linking, authority decisions, protected-object detection | Built and tested (mock directory) |
| Leaver containment with approvals, durable states, manual fallback, SafelyContained verification, non-urgent stages, reconciliation | Built and tested (mock Okta and AD) |
| Tamper-evident audit (hash chain + HMAC), off-box forwarding, health, versioned dual-control configuration | Built and tested (SQLite and PostgreSQL 16) |
| Joiner, password reset, user and computer moves, Okta provisioning, Entra, Exchange, Citrix and Mimecast lifecycle | **Disabled** behind feature flags and typed interfaces |
| Real AD (LDAP as gMSA), live Okta, Windows, IIS and gMSA hosting | Implemented but **unverified**: none were available. Lab and PCATEST procedures are provided. |

What was run, and what passed, is recorded in [FINAL-REPORT.md](FINAL-REPORT.md).

## Quick start (development)

Requirements: the .NET 10 SDK. Python 3 is optional (smoke test), and so is Docker (PostgreSQL).

```bash
dotnet tool restore
dotnet build Ilm.slnx
dotnet test Ilm.slnx
cd src/Ilm.Web && ASPNETCORE_ENVIRONMENT=Development dotnet run
# open https://localhost:5001 and pick an operator on the mock sign-in page
```

The first start creates `src/Ilm.Web/App_Data/ilm-dev.db` (SQLite), seeds 13 fictional people across three forests, and bootstraps the fictional configuration.

| Operator | Role | Try |
|---|---|---|
| Olivia Operator | Lifecycle Operator | Leavers → New → "Casey Placeholder" (legacy-only; contained through the containment-only legacy connector) |
| Adrian Approver | Lifecycle Approver | Approve Olivia's standard leavers |
| Sasha Security | Security Approver | Approve legacy or Tier 0 plans, configuration and feasibility reports |
| Casey Config | Configuration Administrator | Admin → Configuration, Flags, Protection, Feasibility |
| Audrey Auditor | Auditor | Audit → Verify, Export |
| Dale DBA | none | Sees nothing: the DBA gets no lifecycle role |
| Mallory Renamed | none | Holds a group *renamed* to look like `ILM-SecurityApprovers`, and gets no role |

People worth opening:

- Alex (Okta-mastered)
- Bailey (coexistence: three accounts)
- Harper (a provisional email-only link)
- Kai and Lee (Tier 0, never automated)
- Morgan (protection Unknown)
- Jordan (no authority rule, so manual)

Smoke tests:

```bash
scripts/run-smoke.sh            # SQLite
scripts/run-smoke-postgres.sh   # docker compose PostgreSQL, separate migrator and app identities
```

## Repository layout

```
src/
  Ilm.Domain             entities and pure policies: identity model, authority, protection floor,
                         connector modes, leaver state machine, SafelyContained, configuration validator
  Ilm.Application        ports, six provisioning strategies, audit chain, approvals, configuration,
                         protection, reconciliation, Okta-to-AD feasibility harness
  Ilm.Infrastructure     LDAP (System.DirectoryServices.Protocols), mock directory, Okta HTTP client + mock org,
                         session connectors, role resolver, audit forwarders, fictional data
  Ilm.Persistence        EF Core (PostgreSQL + SQLite), migrations, append-only triggers, distributed lock
  Ilm.Web                Razor Pages, OIDC + mock OIDC, health, OpenTelemetry, workers, CLI verbs
  Ilm.Modules.ReadOnly   OU, user, computer and person views, permitted-action explanations
  Ilm.Modules.Leaver     leaver planning, approval binding, containment, verification, manual fallback
tests/
  Ilm.UnitTests  Ilm.IntegrationTests  Ilm.SecurityTests  Ilm.EndToEndTests
deploy/
  postgres/  windows/  docker/
config/bootstrap.development.json   fictional configuration document
docs/                               design baseline from the architecture reviews
```

## Documentation

| Document | Covers |
|---|---|
| [PLAN.md](PLAN.md), [ASSUMPTIONS.md](ASSUMPTIONS.md) | Delivery plan and phases; labelled assumptions |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Projects, dependencies, request path, enabled and disabled capabilities |
| [SECURITY.md](SECURITY.md), [THREAT-MODEL.md](THREAT-MODEL.md) | Controls, authorisation, secrets; STRIDE, abuse cases, residual risks |
| [IDENTITY-AUTHORITY.md](IDENTITY-AUTHORITY.md), [IDENTITY-LINKING.md](IDENTITY-LINKING.md) | Attribute-set authority, containment owner, person and account graph, link confidence |
| [PROTECTED-OBJECTS.md](PROTECTED-OBJECTS.md) | Tier 0 floor, platform identities, attack-path imports, Unknown means deny |
| [LEAVER-WORKFLOW.md](LEAVER-WORKFLOW.md), [MANUAL-CONTAINMENT.md](MANUAL-CONTAINMENT.md) | State machine, SafelyContained, legacy exception, manual fallback and runbooks |
| [OKTA-OIDC.md](OKTA-OIDC.md), [OKTA-AD-PROVISIONING.md](OKTA-AD-PROVISIONING.md), [OKTA-AD-FEASIBILITY-REPORT.md](OKTA-AD-FEASIBILITY-REPORT.md) | Sign-in, issuer migration, roles; the disabled provisioning path and its 26-check PCATEST harness; the current (mock) report |
| [ACTIVE-DIRECTORY.md](ACTIVE-DIRECTORY.md) | LDAP safety, gMSA, SPNs, delegation, network, DNS, time, certificates, recovery, lab procedure |
| [AUDIT.md](AUDIT.md), [APPROVALS.md](APPROVALS.md), [DATABASE-SECURITY.md](DATABASE-SECURITY.md) | Audit chain and forwarding; dual control and validator codes; database controls, backups, DBA risk |
| [DEPLOYMENT.md](DEPLOYMENT.md), [OPERATIONS.md](OPERATIONS.md), [TROUBLESHOOTING.md](TROUBLESHOOTING.md) | Production and development deployment; running the service; fixing problems |
| [DISASTER-RECOVERY.md](DISASTER-RECOVERY.md), [CHANGE-MANAGEMENT.md](CHANGE-MANAGEMENT.md) | Restore and rebuild; how changes are made and gated |
| [TESTING.md](TESTING.md), [FINAL-REPORT.md](FINAL-REPORT.md) | Test suites and traceability; what was executed and what remains unverified |
| [docs/architecture.md](docs/architecture.md), [docs/leaver-containment.md](docs/leaver-containment.md), [docs/open-questions.md](docs/open-questions.md) | The architecture baseline this build implements, and the decisions still open |
