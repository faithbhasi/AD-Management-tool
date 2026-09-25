<#
.SYNOPSIS
  Installs ILM Portal on IIS with the gMSA as the application pool identity. UNVERIFIED in this repository.
.EXAMPLE
  .\Install-IlmPortal.ps1 -SitePath D:\ILM\web -Gmsa 'CORP\svc-ilm$' -HostName ilm.corp.example.test -CertificateThumbprint <thumbprint> -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)] [string] $SitePath,
    [Parameter(Mandatory)] [string] $Gmsa,
    [Parameter(Mandatory)] [string] $HostName,
    [Parameter(Mandatory)] [string] $CertificateThumbprint,
    [string] $SiteName = 'IlmPortal',
    [string] $PoolName = 'IlmPortalPool'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module WebAdministration

if ($PSCmdlet.ShouldProcess($PoolName, 'Create application pool running as the gMSA')) {
    if (-not (Test-Path "IIS:\AppPools\$PoolName")) { New-WebAppPool -Name $PoolName | Out-Null }
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name managedRuntimeVersion -Value ''
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel -Value @{ identityType = 3; userName = $Gmsa; password = '' }
    Set-ItemProperty "IIS:\AppPools\$PoolName" -Name processModel.loadUserProfile -Value $true
}

if ($PSCmdlet.ShouldProcess($SitePath, 'Restrict file system ACLs')) {
    icacls $SitePath /inheritance:r /grant:r "Administrators:(OI)(CI)F" "SYSTEM:(OI)(CI)F" "${Gmsa}:(OI)(CI)RX" | Out-Null
}

if ($PSCmdlet.ShouldProcess($SiteName, 'Create HTTPS-only site')) {
    if (-not (Test-Path "IIS:\Sites\$SiteName")) {
        New-Website -Name $SiteName -PhysicalPath $SitePath -ApplicationPool $PoolName -Port 443 -Ssl -HostHeader $HostName | Out-Null
    }
    $binding = Get-WebBinding -Name $SiteName -Protocol https
    $binding.AddSslCertificate($CertificateThumbprint, 'My')
    Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue | Remove-WebBinding
}

Write-Host 'Inject ILM_OIDC_CLIENT_SECRET (or configure private_key_jwt), ILM_AUDIT_HMAC_KEY and ILM_SIEM_TOKEN from the vault as app pool environment variables.'
Write-Host 'Run migrations with the migration identity: dotnet Ilm.Web.dll migrate, then deploy/postgres/20-grants-after-migration.sql.'
