# Active Directory

ILM reads AD to show OUs, users and computers, to evaluate protection, and to verify containment. It writes to AD in one way only: it sets the `ACCOUNTDISABLE` bit on standard user accounts in forests whose connector is `ContainmentOnlyLegacy`. Or, once enabled and approved, it does the same in a target forest in `WriteTarget` mode. It runs as a gMSA with narrowly delegated rights. It stores no bind password and needs no Domain Admin.

> **Status.** The LDAP connector (`LdapDirectoryConnector`) is compiled and unit-tested for filter escaping, DN handling, SID conversion and error classification. It has **not** been run against a real domain controller: no AD was available where this repository was built. Everything below marked *lab* must be validated with the [lab procedure](#lab-validation-procedure-phase-3) before any production use.

## How ILM talks to AD

| Concern | Implementation |
|---|---|
| Library | `System.DirectoryServices.Protocols` (`LdapConnection`) |
| Authentication | `AuthType.Negotiate` with a **null credential**, which uses the process identity (the gMSA). Simple binds are never used. |
| Transport | LDAPS on 636, with chain, revocation and host-name validation (`LdapConnectionFactory.ValidateCertificate`), **or** Kerberos signing and sealing on 389 (Windows only). |
| Addressing | `<GUID=…>` and `<SID=…>` extended DNs. DNs typed in the UI are never used. OU scopes are objectGUIDs, resolved to a DN at request time. |
| Filters | Built only through `LdapFilter` (RFC 4515 escaping), and filtering is done by the server. |
| Attributes | Only `SafeAttributeAllowlist` is requested. Password, LAPS and gMSA-password attributes are never requested. |
| Paging | Paged results with a page size limit, plus timeouts (5–120 s) and cancellation |
| DC selection | One writable DC per workflow, chosen from the connector's `DomainControllers`. ILM checks the DC answers, that `defaultNamingContext` and `rootDomainNamingContext` match the connector's domain and forest, and that the DC isn't an RODC (`msDS-isRODC`). The DC is recorded on the action and in the audit record. |
| Commit-time recheck | `GuardedDirectoryWriter` rereads the object and re-evaluates protection and connector mode **on the pinned DC** just before writing. |
| Compare-and-swap | One modify that deletes the old `userAccountControl` value and adds `old | 0x2`. If another writer changed it first, AD rejects the request, and ILM rereads and retries at most 3 times. |
| Idempotency and locks | Idempotency keys per action, plus a lease-based per-person distributed lock stored in the database (`EfDistributedLock`) |
| Errors | `LdapErrorClassifier` maps result codes to safe categories. Native duplicate errors (`entryAlreadyExists`, `constraintViolation` with `ERROR_DS_NAME_ALREADY_EXISTS`) become `DuplicateObject`. `noSuchAttribute` becomes `ConcurrencyConflict` (a lost compare-and-swap). `insufficientAccessRights` becomes `InsufficientDirectoryRights`. `busy` and `unavailable` become `ConnectorUnavailable`. |
| Nested groups | `LDAP_MATCHING_RULE_IN_CHAIN` (`1.2.840.113556.1.4.1941`) within the domain. Cross-forest membership is resolved through foreign security principals. |
| Out-of-band changes | Reconciliation rereads every known identity and raises a Critical alert if a contained account is enabled again |

## gMSA

### Prerequisites

- Forest and domain functional level Windows Server 2012 or later, and at least one Windows Server 2012 or later DC in the gMSA's domain.
- A **KDS root key** in the forest. In production run `Add-KdsRootKey`, then wait for replication before first use (the default effective time is 10 hours after creation). In an isolated lab only, you may use `Add-KdsRootKey -EffectiveTime ((Get-Date).AddHours(-10))`.
- A dedicated security group of ILM host computer accounts, for example `ILM-Portal-Hosts`.

### Creating it

Run `deploy/windows/New-IlmGmsa.ps1` as a Tier 0 administrator from a PAW:

```powershell
.\New-IlmGmsa.ps1 -Name svc-ilm -DnsHostName ilm.corp.example.test -HostGroup ILM-Portal-Hosts -WhatIf   # review
.\New-IlmGmsa.ps1 -Name svc-ilm -DnsHostName ilm.corp.example.test -HostGroup ILM-Portal-Hosts
```

The script:

- sets `PrincipalsAllowedToRetrieveManagedPassword` to the host group only (never to users or broad groups)
- sets `KerberosEncryptionType` to AES256
- grants no group memberships

Then, on each ILM host:

```powershell
Install-ADServiceAccount -Identity svc-ilm
Test-ADServiceAccount -Identity svc-ilm   # must return True
```

### Allowed hosts, and what ILM protects

- Only computer accounts in `ILM-Portal-Hosts` may retrieve the password.
- Add these to `Ilm:Platform` so they're on the protection floor ([PROTECTED-OBJECTS.md](PROTECTED-OBJECTS.md)):
  - the gMSA SID (`RuntimeIdentitySids`)
  - each host's computer SID (`HostComputerSids`)
  - the host group SID (`ManagedPasswordRetrieverSids`)
  - then set `ManagedPasswordRetrieversVerified=true` after checking `Get-ADServiceAccount svc-ilm -Properties PrincipalsAllowedToRetrieveManagedPassword`
- Membership of `ILM-Portal-Hosts` is Tier 0-controlled. Anyone who can add a computer to it can become ILM.

### SPNs

The gMSA needs **no SPN** of its own. Operators authenticate with Okta OIDC, not Kerberos to IIS, so don't register an `HTTP/` SPN on it. Outbound Kerberos uses the DCs' `ldap/` SPNs. If PostgreSQL uses Kerberos (`gss`), register `postgres/pgsql01.corp.example.test` on the **database service account**, not on the ILM gMSA. After deployment, check `setspn -L svc-ilm` is empty, and `setspn -X` reports no duplicates.

### IIS

Run `deploy/windows/Install-IlmPortal.ps1`. It:

- sets the app pool identity to `CORP\svc-ilm$` with an empty password (the gMSA pattern)
- restricts file-system ACLs to Administrators, SYSTEM and the gMSA (read and execute)
- creates an HTTPS-only binding

IIS adds the pool identity to `IIS_IUSRS`, which gives it the logon right it needs. See [DEPLOYMENT.md](DEPLOYMENT.md).

## Delegated permissions

Run `deploy/windows/Grant-IlmDelegation.ps1` (with `-WhatIf` first). It refuses the domain root, Domain Controllers, and AdminSDHolder.

| Scope | Right | Why |
|---|---|---|
| Read-only OUs (every forest) | Read properties on `user` and `computer` objects (often already covered by Authenticated Users) | Views, protection, verification |
| `ContainmentOnlyLegacy` OUs | **Write property `userAccountControl` on `user` objects only**, inherited to descendants | Setting the disable bit |
| Anything else | **Nothing.** No create or delete child, reset password, write `member`, move, or write any other property. | |

AD permissions can't restrict *which bit* of `userAccountControl` is written. ILM enforces "set ACCOUNTDISABLE only" in code (`WithDisableBitSet` and the compare-and-swap). Two more safeguards:

- The SIEM should alert on any **4722 (account enabled)** or **4738** event whose subject is the ILM gMSA.
- Protected accounts (`adminCount=1`) have inheritance disabled by SDProp, so OU delegation doesn't reach them. That's defence in depth on top of the protection floor.

## Network

| From ILM host to | Port | Purpose |
|---|---|---|
| Each configured DC | TCP 636, or TCP 389 with sign and seal | LDAP |
| DCs | TCP/UDP 88, TCP 464 | Kerberos, and gMSA password retrieval support |
| DNS servers | TCP/UDP 53 | Name resolution for every forest |
| Time source (domain hierarchy) | UDP 123 | Kerberos (5-minute skew) |
| CRL and OCSP endpoints of the forest CAs | TCP 80/443 | LDAPS certificate revocation checks (`RevocationMode.Online`) |
| PostgreSQL | TCP 5432 | Database, TLS `VerifyFull` |
| Okta org | TCP 443 | OIDC and management API |
| SIEM collector | TCP 443 | Audit forwarding |

Inbound: TCP 443 from the operators' networks only, ideally through a PAM or jump network.

### DNS

The host must resolve every configured DC's FQDN in every forest. Use conditional forwarders or stub zones for the legacy forests. Names in `DomainControllers` must match each DC's certificate subject or SAN, and they must be FQDNs, not IP addresses.

### Time

The host must sync with the domain hierarchy (`w32tm /query /status`). If the skew is more than 5 minutes, Kerberos binds fail, and ILM shows the connector as unavailable.

### Certificate trust

- Each DC's LDAPS certificate must chain to a CA in `LocalMachine\Root` or `LocalMachine\CA` on the ILM host, and its name must match the DC FQDN.
- Legacy forests usually have their own enterprise CA. Import that forest's root into `LocalMachine\Root` on the ILM hosts only.
- Revocation is checked online. Unreachable CRLs fail the bind closed.
- Validation is implemented for Windows hosts. On Linux (development only), OpenLDAP validates against the system trust store.

## Recovery

| Situation | Action |
|---|---|
| gMSA password retrieval fails on a host (`Test-ADServiceAccount` False) | Check the host is in `ILM-Portal-Hosts` (reboot after adding it to refresh the Kerberos ticket), the KDS root key exists, and a 2012+ DC is reachable. Run `Reset-ComputerMachinePassword` only if the secure channel is broken. |
| gMSA deleted or compromised | Create a new gMSA with a new name (for example `svc-ilm2`), re-run the delegation with the new principal, **remove** the old principal's ACEs from every delegated OU, update `Ilm:Platform` SIDs, and redeploy. Review Security log 4662/5136 for the old identity. |
| Delegation removed by mistake | ILM can't write. Containment falls back to manual tasks automatically. Re-run `Grant-IlmDelegation.ps1` through change control. |
| DC decommissioned | Remove it from `DomainControllers` in a new configuration version, which needs approval. ILM skips unreachable DCs when choosing one. |
| Host rebuilt | Join it to the domain, add it to `ILM-Portal-Hosts`, `Install-ADServiceAccount`, and update `HostComputerSids`. |

## Lab validation procedure (Phase 3)

Use an **isolated** lab: no trust to production, synthetic accounts only, and names under `example.test`. Record each result, with evidence, in `docs/lab-results.md` and in [ASSUMPTIONS.md](ASSUMPTIONS.md).

### 1. Build

1. Build two forests on Windows Server 2022 or later DCs:
   - `corp.example.test` (target), with DCs `dc01` and `dc02` plus one RODC `rodc01`
   - `legacy-a.example.test`, with DC `lgadc01`
   - a two-way forest trust between them
2. Install an enterprise CA in each forest and enrol a *Kerberos Authentication* or *Domain Controller* certificate on each DC, so LDAPS works.
3. Create the OUs used in `config/bootstrap.development.json`, and record each OU's objectGUID.
4. Create synthetic users:
   - standard users in the managed OUs
   - one disabled user
   - one user in Domain Admins (a floor test)
   - one user with `adminCount=1`
   - one member of a nested group inside Server Operators
   - a legacy-a user placed through an FSP in a target-forest group that is nested in a protected group
   - one computer with unconstrained delegation
5. Create the gMSA and the host group, and join a Windows Server 2022 member server `ilm-lab01` to `corp.example.test`.

### 2. Deploy

1. Deploy PostgreSQL (see [DATABASE-SECURITY.md](DATABASE-SECURITY.md)) and ILM to `ilm-lab01` with `Install-IlmPortal.ps1`.
2. Run `Grant-IlmDelegation.ps1` for the legacy-a containment OU.
3. Change each connector's `Implementation` to `Ldap` in a configuration version, and approve and activate it.

### 3. Tests

Record pass or fail and evidence for each.

| # | Test | Expected | Proves |
|---|---|---|---|
| L1 | `/health/ready` with the connectors configured | `connectors: Healthy` | Negotiate bind as the gMSA with no password (A5) |
| L2 | Packet capture of the LDAP traffic | TLS on 636, or sealed SASL on 389, and no simple bind | Transport |
| L3 | Replace a DC certificate with one for the wrong name, or an untrusted one | Bind fails, connector Unavailable | Certificate validation |
| L4 | Browse OUs, search users, open a user | Data matches `Get-ADUser`, and no password, LAPS or gMSA attributes appear | Allowlist, paging, `<GUID=>` addressing (A11) |
| L5 | Open the Domain Admins member, the `adminCount` user, the nested Server Operators member and the FSP-nested legacy user | Each shows **Protected** | Floor, in-chain matching, FSP handling |
| L6 | Leaver for a legacy-a standard user (Security Approver approves) | `userAccountControl` 512 → 514 on the recorded DC. SafelyContained. Security log 4725 with subject `svc-ilm$`. | ContainmentOnlyLegacy write, DC pinning |
| L7 | Repeat L6 with the account already disabled | No write, Verified | `AlreadyInDesiredState` |
| L8 | Set `DomainControllers` to only `rodc01` | DC selection refuses, manual task | RODC check |
| L9 | Between ILM's read and write, change `userAccountControl` from another session (use a debugger breakpoint or a slow DC) | Modify rejected, ILM rereads and retries | Compare-and-swap (A12) |
| L10 | Remove the delegation, then run a leaver | `insufficientAccessRights` classified, manual task, no silent failure | Error classification, failure direction |
| L11 | Leaver for the Domain Admins member | No write attempted, Tier 0 task, Critical alert | Floor enforced end to end |
| L12 | Re-enable a contained account from ADUC, then run reconciliation | Critical `ContainedAccountReEnabled` alert. The request moves to ReconciliationRequired. | Out-of-band detection |
| L13 | Stop `lgadc01`, then run a leaver for a legacy-a user | FailedBeforeChange, then a manual task | Outage handling |
| L14 | `setspn -L svc-ilm`, `Get-ADServiceAccount -Properties *` | No SPN. Only the host group can retrieve the password. | gMSA hygiene |
| L15 | (Only if creation is ever enabled) LDAP add with `userAccountControl=514` and no password | Record whether the DC accepts it or needs 546 | A15 |

### 4. Clean-up

Disable the lab users and delete them manually (ILM never deletes). Revert the delegation, and destroy the lab snapshot.
