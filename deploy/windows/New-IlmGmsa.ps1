<#
.SYNOPSIS
  Creates the ILM Portal gMSA. Run by a Tier 0 administrator from a PAW. UNVERIFIED in this repository (no AD available).
.DESCRIPTION
  - Requires a KDS root key (Add-KdsRootKey; in production allow 10 hours for replication before first use).
  - Only members of the ILM host group can retrieve the managed password (PrincipalsAllowedToRetrieveManagedPassword).
  - The gMSA gets no group memberships and no rights here; OU delegation is a separate, reviewed step.
.EXAMPLE
  .\New-IlmGmsa.ps1 -Name svc-ilm -DnsHostName ilm.corp.example.test -HostGroup ILM-Portal-Hosts -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $DnsHostName,
    [Parameter(Mandatory)] [string] $HostGroup
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module ActiveDirectory

if (-not (Get-KdsRootKey)) {
    throw 'No KDS root key exists. A Tier 0 administrator must create one (Add-KdsRootKey) and wait for replication.'
}

$group = Get-ADGroup -Identity $HostGroup
if ($PSCmdlet.ShouldProcess($Name, 'Create gMSA')) {
    New-ADServiceAccount -Name $Name -DNSHostName $DnsHostName `
        -PrincipalsAllowedToRetrieveManagedPassword $group `
        -KerberosEncryptionType AES256 `
        -Description 'ILM Portal runtime identity (Tier 1). Managed by change control.'
}

Write-Host "Next, on each ILM host (member of $HostGroup):"
Write-Host "  Install-ADServiceAccount -Identity $Name ; Test-ADServiceAccount -Identity $Name"
Write-Host "Record the gMSA SID in Ilm:Platform:RuntimeIdentitySids and the host group SID in ManagedPasswordRetrieverSids."
