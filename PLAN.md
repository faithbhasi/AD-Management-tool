# ILM Portal delivery plan

This plan turns the design in [`docs/architecture.md`](docs/architecture.md) and [`docs/leaver-containment.md`](docs/leaver-containment.md) into a working application. It follows the build brief's section 22 phases. Phases 3 onward need infrastructure this repository can't provide (lab AD, PCATEST Okta), so they ship as procedures and harnesses rather than results.

## Solution

```
src/
  Ilm.Domain              entities, enums, pure policies (protection floor, authority, state machine, SafelyContained)
  Ilm.Application         ports, strategies, audit chain, approvals, configuration, reconciliation, feasibility harness
  Ilm.Infrastructure      LDAP (System.DirectoryServices.Protocols), mock AD, Okta HTTP + mock, session connectors, forwarders
  Ilm.Persistence         EF Core context, SQLite (dev) and PostgreSQL (prod) migrations, distributed lock
  Ilm.Web                 Razor Pages UI, OIDC + dev mock OIDC, authorisation, health, telemetry, workers, CLI verbs
  Ilm.Modules.ReadOnly    OU browsing, user/computer/person views, permitted-action evaluation
  Ilm.Modules.Leaver      leaver request, approval, containment planning/execution/verification, non-urgent stages
tests/
  Ilm.UnitTests           pure domain and application logic
  Ilm.IntegrationTests    persistence, workflow with mocks, feasibility harness with mocks, PostgreSQL (when available)
  Ilm.SecurityTests       authentication, authorisation, configuration rejection, protection floor, secrets, DB rights
  Ilm.EndToEndTests       full HTTP flow through the mock OIDC provider and Razor Pages
```

No other projects are created. Integration projects (Graph, Citrix, Mimecast, Exchange) are added only when those integrations are implemented.

## Phases

| Phase | Scope | Deliverable in this repository | Status |
|---|---|---|---|
| 0 | Architecture, threat model, solution, database, audit, feature flags, mock connectors | Code, migrations, docs | Implemented |
| 1 | Mock OIDC, roles, read-only portal, person/identity model, authority rules, OU scopes, protection floor, user and computer views | Code, tests | Implemented |
| 2 | Leaver request, approval, manual fallback, containment-only legacy strategy, mock Okta and AD containment, session connectors, SafelyContained verification, reconciliation | Code, tests | Implemented with mocks |
| 3 | Isolated lab AD, gMSA validation, synthetic accounts, containment testing | [`ACTIVE-DIRECTORY.md`](ACTIVE-DIRECTORY.md) lab procedure, deployment scripts | Procedure only: no lab AD available |
| 4 | PCATEST Okta-to-AD feasibility harness and report | Harness, mock run, report generator, PCATEST procedure | Harness built. PCATEST not run. |
| 5 | Controlled pilot after approval | [`CHANGE-MANAGEMENT.md`](CHANGE-MANAGEMENT.md) | Not started |
| 6 | Joiner module, one provisioning strategy per population | Strategy code exists behind disabled flags | Not started |

## Execution order

1. Write the plan, the assumptions and the threat model.
2. Scaffold the solution with central package management and analysers.
3. Domain: the identity model, attribute sets, authority, protection floor, connector modes, leaver state machine and SafelyContained policy.
4. Application and persistence: ports, strategies, the audit chain, approvals, configuration versions, migrations.
5. Infrastructure: the LDAP connector, mock directory, Okta HTTP client and mock, session connectors, the role resolver and audit forwarders.
6. Web and modules: pages, OIDC with the development mock, workers, health checks and CLI verbs.
7. Tests for every item in the brief's section 21.
8. Formatting, static analysis, secret scanning, build, tests, running the app, smoke tests.
9. The documentation set and the final report.

## Out of scope for this delivery

- Any production activation. Okta provisioning stays disabled until a feasibility report is approved.
- Real connectors for Entra, Exchange, Citrix, VPN and Mimecast. They have typed interfaces, report `NotConfigured`, and generate manual tasks.
- Joiner, mover, password reset, user move and computer disable/move. Their interfaces and strategies exist behind disabled feature flags.
