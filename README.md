# ILM Portal (Identity Lifecycle Manager)

ILM Portal is a web application that manages the lifecycle of joiners, movers and leavers, admin accounts and computer objects. It works across a target Active Directory forest, several legacy forests during consolidation, and Okta.

**Status:** architecture phase. There's no code yet.

## Documents

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Architecture baseline, revision 3. Covers source of authority, forest scope, operator sign-in, hosting, Tier 0 protection, uniqueness, account creation, audit, orchestration, solution structure, phasing and the amendments to spec v2. |
| [docs/leaver-containment.md](docs/leaver-containment.md) | Phase 1b specification. Covers containment stages, the owner for each account type, verification levels, failure direction, the legacy containment exception (rule 6a), the case state machine and acceptance scenarios. |
| [docs/open-questions.md](docs/open-questions.md) | Decisions and checks the design depends on, each with an owner and the phase it blocks. |

## Phases

| Phase | Scope |
|---|---|
| 1a | Okta OIDC sign-in, read-only views, audit pipeline |
| 1b | Leaver containment |
| 2 | Joiner (standard users, through the Okta API) |
| 3 | Mover |
| 4 | Admin accounts and computers (direct LDAP) |
