<#
.SYNOPSIS
Packs a version of the Windows app into a local update feed, for trying installs and updates without a release.

.DESCRIPTION
Publishes and packs the app as CI's release job does. See docs/design/windows-app.md#trying-updates-locally.

.EXAMPLE
./tools/Publish-TestUpdate.ps1 -Version 0.0.1-local -Reset -Install
./tools/Publish-TestUpdate.ps1 -Version 0.0.2-local
#>
param(
    # Must be higher than the last version in the feed
    [Parameter(Mandatory)]
    [string]$Version,

    [string]$Runtime = 'win-x64',

    # Deletes the feed first
    [switch]$Reset,

    # Installs this version and points it at the feed
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) 'obm-update-test'
$feed = Join-Path $work 'releases'
$publish = Join-Path $work "publish-$Version"

if ($Reset -and (Test-Path $work)) {
    Remove-Item $work -Recurse -Force
}

$publishArgs = @(
    'publish', (Join-Path $root 'src/OpenBackupManager.App.Windows')
    '--configuration', 'Release'
    '--runtime', $Runtime
    '--self-contained'
    "-p:Version=$Version"
    '--output', $publish
    '--nologo'
)
dotnet @publishArgs
if ($LASTEXITCODE) { throw 'Publishing failed' }

$packArgs = @(
    'vpk', 'pack'
    '--packId', 'OpenBackupManager.App'
    '--packVersion', $Version
    '--packTitle', 'OpenBackupManager'
    '--packAuthors', 'Amy Jeanes'
    '--packDir', $publish
    '--mainExe', 'OpenBackupManager.App.Windows.exe'
    '--icon', (Join-Path $root 'src/OpenBackupManager.App.Windows/Assets/AppIcon.ico')
    '--shortcuts', 'StartMenuRoot'
    '--runtime', $Runtime
    '--channel', $Runtime
    '--outputDir', $feed
)
dotnet @packArgs
if ($LASTEXITCODE) { throw 'Packing failed' }

if ($Install) {
    $setup = Join-Path $feed "OpenBackupManager.App-$Runtime-Setup.exe"
    Start-Process $setup -ArgumentList '--silent' -Wait

    # Kept through updates, removed on uninstall
    $installed = Join-Path $env:LOCALAPPDATA 'OpenBackupManager.App'
    Set-Content (Join-Path $installed 'update-source') $feed

    Start-Process (Join-Path $installed 'current/OpenBackupManager.App.Windows.exe')
    Write-Host "Installed $Version, checking $feed for updates"
}
