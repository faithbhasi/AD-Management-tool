# Assumptions

Each assumption is labelled with how it was established.

| Label | Meaning |
|---|---|
| **Decided** | Taken from the build brief or the earlier design documents in [`docs/`](docs/). |
| **Assumed** | Chosen here because nothing specified it. Revisit it. |
| **Unverified** | Vendor or environment behaviour that hasn't been tested. It needs lab or PCATEST evidence. |

## Platform

| # | Assumption | Label |
|---|---|---|
| A1 | .NET 10 is the current supported LTS (released November 2025, supported to November 2028). The build uses SDK 10.0.x. | Decided |
| A2 | Production runs on Windows Server under IIS with the ASP.NET Core Module, with the app pool identity set to a gMSA. | Decided |
| A3 | PostgreSQL 16 or later is the production database. SQLite is used only for isolated development and tests. | Decided |
| A4 | One ILM web instance writes the audit chain at a time. With PostgreSQL, a transaction-scoped advisory lock serialises appends across instances. | Assumed |
| A5 | `System.DirectoryServices.Protocols` on Windows can bind with the process's Kerberos identity (the gMSA) and no stored password. | Unverified. No AD was available here. |

## Identity

| # | Assumption | Label |
|---|---|---|
| A6 | No HR system provides an immutable person identifier. ILM issues its own (`PersonIdentifierSource = IlmIssued`), and `EmployeeIdentifier` stays optional. | Decided |
| A7 | Okta `sub` for a user is the Okta user ID, and it's stable for the life of the Okta user. | Unverified |
| A8 | The org authorization server versus a custom authorization server is still undecided. The configured issuer is exact, and changing it is an identity migration. | Decided (the choice itself is pending) |
| A9 | Okta group IDs are immutable, and group names can change. | Decided |
| A10 | Okta scope names for the service app (`okta.users.manage`, `okta.users.read`, `okta.groups.read`, `okta.groups.manage`, `okta.apps.read`, `okta.logs.read`) are configuration. Check them against the org before use. | Unverified |

## Directory

| # | Assumption | Label |
|---|---|---|
| A11 | AD accepts `<GUID=...>` extended DNs for base searches and modifications. ILM uses them so operations target stable IDs, not DNs typed in by a user. | Unverified in lab |
| A12 | A single LDAP modify that deletes the old `userAccountControl` value and adds the new one fails if the value changed in between, giving compare-and-swap semantics. | Unverified in lab |
| A13 | `LDAP_MATCHING_RULE_IN_CHAIN` (1.2.840.113556.1.4.1941) doesn't cross forests. Cross-forest membership is resolved through foreign security principals. | Decided |
| A14 | Disabling an AD account doesn't end existing Kerberos tickets, cached logons or application sessions. | Decided (from the design reviews) |
| A15 | Whether a DC accepts an LDAP add with `userAccountControl=514` and no password is untested. Creation is disabled here, and the plan records both outcomes. | Unverified |

## Okta-to-AD provisioning

| # | Assumption | Label |
|---|---|---|
| A16 | Creating an Okta user doesn't by itself create an AD user. The group or app assignment that drives AD provisioning decides that. | Decided (to be proven by the PCATEST harness) |
| A17 | Okta deactivation disables the pushed AD account only if AD deprovisioning is enabled in the integration. Suspension is expected not to touch AD. | Unverified. The harness tests both. |
| A18 | The provisioning delay isn't fixed. The harness records it and never hardcodes it. | Decided |

## Development environment

| # | Assumption | Label |
|---|---|---|
| A19 | All development data is fictional and uses `example.test` names, fictional SIDs and GUIDs, and synthetic Okta IDs. | Decided |
| A20 | The development mock OIDC provider runs inside the web process. Startup refuses to enable it outside the Development environment. | Assumed |
| A21 | The development mock OIDC client secret and signing key are generated at startup and held only in memory. | Assumed |
