<#
.SYNOPSIS
Publishes the Windows app, obmd and obm into one folder, laid out as they're installed.

.DESCRIPTION
Each is compiled ahead of time with NativeAOT. The app and obmd go in the folder, and obm alone in its bin folder, which
installing adds to PATH. On macOS and Linux, only obmd and obm. CI's release and test jobs and Publish-TestUpdate.ps1
publish with it.

.EXAMPLE
./tools/Publish-App.ps1 -Output publish -Version 0.1.0
#>
param(
    [Parameter(Mandatory)]
    [string]$Output,

    [string]$Runtime = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

    # Debug for the tests, which crash the app and the engine through debug-only menus and methods
    [string]$Configuration = 'Release',

    [string]$Version = '0.0.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot

function Publish($project, $folder) {
    $publishArgs = @(
        'publish', (Join-Path $root "src/$project")
        '--configuration', $Configuration
        '--runtime', $Runtime
        "-p:Version=$Version"
        # No symbol files, which compiled code doesn't read, and NativeAOT's are bigger than the programs
        '-p:DebugType=none'
        '--output', $folder
        '--nologo'
    )
    dotnet @publishArgs
    if ($LASTEXITCODE) { throw "Publishing $project failed" }
}

if ($IsWindows) {
    Publish 'OpenBackupManager.App.Windows' $Output
}
Publish 'OpenBackupManager.Daemon' $Output
Publish 'OpenBackupManager.Cli' (Join-Path $Output 'bin')
