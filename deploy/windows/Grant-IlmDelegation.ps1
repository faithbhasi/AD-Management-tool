<#
.SYNOPSIS
  Grants the ILM gMSA the narrow OU rights it needs. UNVERIFIED in this repository (no AD available).
.DESCRIPTION
  Read-only scopes: generic read on user and computer objects (default authenticated-user read usually suffices).
  Containment-only legacy scopes: Write Property on userAccountControl for user objects ONLY.
  Nothing else is granted: no create/delete child, no reset password, no group membership writes,
  and never on the domain root, AdminSDHolder, the Domain Controllers OU, or any Tier 0 OU.
  AD permissions cannot restrict the direction of a userAccountControl write, so ILM enforces
  "set the disable bit only" in code and the SIEM alerts on any enable (event 4722) by this identity.
.EXAMPLE
  .\Grant-IlmDelegation.ps1 -Gmsa 'LEGACYA\svc-ilm$' -ContainmentOu 'OU=Legacy Staff,DC=legacy-a,DC=example,DC=test' -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)] [string] $Gmsa,
    [string[]] $ContainmentOu = @(),
    [string[]] $ReadOnlyOu = @()
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($ou in $ContainmentOu) {
    if ($ou -match '^DC=' -or $ou -match 'OU=Domain Controllers' -or $ou -match 'CN=AdminSDHolder') {
        throw "Refusing to delegate on $ou (domain root or Tier 0 container)."
    }
    if ($PSCmdlet.ShouldProcess($ou, "Grant WP userAccountControl on user objects to $Gmsa")) {
        dsacls $ou /I:S /G "${Gmsa}:WP;userAccountControl;user" | Out-Null
        dsacls $ou /I:S /G "${Gmsa}:RP;;user" | Out-Null
    }
}

foreach ($ou in $ReadOnlyOu) {
    if ($PSCmdlet.ShouldProcess($ou, "Grant read on users and computers to $Gmsa")) {
        dsacls $ou /I:S /G "${Gmsa}:RP;;user" | Out-Null
        dsacls $ou /I:S /G "${Gmsa}:RP;;computer" | Out-Null
    }
}
