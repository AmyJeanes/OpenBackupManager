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

    # The folder the versions are packed into
    [string]$Feed = (Join-Path ([IO.Path]::GetTempPath()) 'obm-update-test/releases'),

    # Deletes the feed first
    [switch]$Reset,

    # Installs this version and points it at the feed
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$Feed = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Feed)
$publish = Join-Path ([IO.Path]::GetTempPath()) "obm-update-test/publish-$Version"

if ($Reset -and (Test-Path $Feed)) {
    Remove-Item $Feed -Recurse -Force
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
    '--noPortable'
    '--runtime', $Runtime
    '--channel', $Runtime
    '--outputDir', $Feed
)
dotnet @packArgs
if ($LASTEXITCODE) { throw 'Packing failed' }

if ($Install) {
    $setup = Join-Path $Feed "OpenBackupManager.App-$Runtime-Setup.exe"
    Start-Process $setup -ArgumentList '--silent' -Wait

    # Kept through updates, removed on uninstall
    $installed = Join-Path $env:LOCALAPPDATA 'OpenBackupManager.App'
    Set-Content (Join-Path $installed 'update-source') $Feed

    Start-Process (Join-Path $installed 'current/OpenBackupManager.App.Windows.exe')
    Write-Host "Installed $Version, checking $Feed for updates"
}
