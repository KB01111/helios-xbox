<#
.SYNOPSIS
Installs Helios Launcher and optionally makes it the Windows Xbox mode home app.

.DESCRIPTION
Helios declares the windows.gamingApp extension and the Microsoft.appCategory.gamingHome custom capability.
Together they list Helios under Settings > Gaming > Xbox mode > Choose home app. Windows accepts the
capability's developer descriptor (CustomCapability.SCCD) only while Developer Mode is on. This script turns
Developer Mode on for the install if needed and restores the previous setting afterwards. Run it from an
elevated PowerShell prompt that is signed in as the user who will play.

.EXAMPLE
.\Install-Helios.ps1 -Package .\Helios-Launcher-0.2.0-x64.msix -Certificate .\Helios-Preview.cer -SetAsXboxHome -StartInXboxMode

.EXAMPLE
.\Install-Helios.ps1 -RestoreXboxApp
#>
[CmdletBinding(DefaultParameterSetName = 'Install')]
param(
    [Parameter(ParameterSetName = 'Install', Mandatory)]
    [string] $Package,

    [Parameter(ParameterSetName = 'Install')]
    [string] $Certificate,

    [Parameter(ParameterSetName = 'Install')]
    [switch] $SetAsXboxHome,

    [Parameter(ParameterSetName = 'Install')]
    [Parameter(ParameterSetName = 'Home')]
    [switch] $StartInXboxMode,

    [Parameter(ParameterSetName = 'Home', Mandatory)]
    [switch] $HomeOnly,

    [Parameter(ParameterSetName = 'Restore', Mandatory)]
    [switch] $RestoreXboxApp
)

$ErrorActionPreference = 'Stop'
$PackageName = 'KB01111.HeliosLauncher'
$XboxApp = 'Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App'
$GamingKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration'
$UnlockKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Set-GamingHome([string] $AppId, [Nullable[bool]] $Startup) {
    if (-not (Test-Path $GamingKey)) { New-Item -Path $GamingKey -Force | Out-Null }
    Set-ItemProperty -Path $GamingKey -Name GamingHomeApp -Value $AppId -Type String
    if ($null -ne $Startup) {
        Set-ItemProperty -Path $GamingKey -Name StartupToGamingHome -Value ([int]$Startup) -Type DWord
    }
    Write-Host "Xbox mode home set to $AppId$(if ($null -ne $Startup) { ", start in Xbox mode: $Startup" })."
}

function Get-HeliosAppId {
    $installed = Get-AppxPackage -Name $PackageName | Select-Object -First 1
    if (-not $installed) { throw 'Helios Launcher is not installed for this user.' }
    "$($installed.PackageFamilyName)!App"
}

switch ($PSCmdlet.ParameterSetName) {
    'Restore' {
        Set-GamingHome $XboxApp $null
        Write-Host 'Xbox mode will open the Xbox app again. Helios stays installed.'
        return
    }
    'Home' {
        Set-GamingHome (Get-HeliosAppId) ([bool]$StartInXboxMode)
        return
    }
}

if (-not (Test-Administrator)) {
    throw 'Run this script from an elevated PowerShell prompt. Installing the certificate and toggling Developer Mode need administrator rights.'
}
$Package = (Resolve-Path $Package).Path

if ($Certificate) {
    $Certificate = (Resolve-Path $Certificate).Path
    Import-Certificate -FilePath $Certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Write-Host 'Trusted the Helios preview certificate (Local Machine > Trusted People).'
}

$previous = (Get-ItemProperty -Path $UnlockKey -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
$enabledHere = $previous -ne 1
try {
    if ($enabledHere) {
        if (-not (Test-Path $UnlockKey)) { New-Item -Path $UnlockKey -Force | Out-Null }
        Set-ItemProperty -Path $UnlockKey -Name AllowDevelopmentWithoutDevLicense -Value 1 -Type DWord
        Write-Host 'Developer Mode turned on for the install.'
    }
    Add-AppxPackage -Path $Package -ForceApplicationShutdown
    Write-Host 'Helios Launcher installed.'
}
finally {
    if ($enabledHere) {
        if ($null -eq $previous) {
            Remove-ItemProperty -Path $UnlockKey -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
        } else {
            Set-ItemProperty -Path $UnlockKey -Name AllowDevelopmentWithoutDevLicense -Value $previous -Type DWord
        }
        Write-Host 'Developer Mode restored to its previous setting.'
    }
}

if ($SetAsXboxHome) {
    Set-GamingHome (Get-HeliosAppId) $(if ($StartInXboxMode) { $true } else { $null })
    Write-Host 'Check Settings > Gaming > Xbox mode: Helios should be the selected home app.'
} else {
    Write-Host 'Choose Helios under Settings > Gaming > Xbox mode > Choose home app, or rerun with -SetAsXboxHome.'
}
