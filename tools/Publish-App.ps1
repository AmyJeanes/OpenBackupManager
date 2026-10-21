<#
.SYNOPSIS
Publishes the Windows app, obmd and obm into one folder, laid out as they're installed.

.DESCRIPTION
Each is compiled ahead of time with NativeAOT. On Windows, the app and obmd go in the folder, and obm alone in its bin
folder, which installing adds to PATH. On macOS and Linux, obm and obmd go side by side, as the tarball has them. CI's
package and test jobs and Publish-TestUpdate.ps1 publish with it.

.EXAMPLE
./tools/Publish-App.ps1 -Output publish -Version 0.1.0
#>
param(
    [Parameter(Mandatory)]
    [string]$Output,

    [string]$Runtime = [Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier,

    # Debug for the tests, which crash the app and the engine through debug-only menus and methods
    [string]$Configuration = 'Release',

    [string]$Version = '0.0.0',

    # Linux only: links against an older system's libraries in this folder, so the programs also run on older distros.
    # .NET's cross-build images have them in /crossrootfs/<architecture>
    [string]$SysRoot
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
    if ($SysRoot) {
        $publishArgs += "-p:SysRoot=$SysRoot", '-p:LinkerFlavor=lld'
    }
    dotnet @publishArgs
    if ($LASTEXITCODE) { throw "Publishing $project failed" }
}

if ($IsWindows) {
    Publish 'OpenBackupManager.App.Windows' $Output
}
Publish 'OpenBackupManager.Daemon' $Output
Publish 'OpenBackupManager.Cli' ($IsWindows ? (Join-Path $Output 'bin') : $Output)
