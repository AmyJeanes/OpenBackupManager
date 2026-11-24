<#
.SYNOPSIS
Closes the windows GitHub's Windows runners leave holding the foreground, and stops WSL's update prompt opening
later, so the UI tests' app can take it, then waits for the startup apps that closing them lets Windows run. Remove
once https://github.com/actions/runner-images/issues/14069 and https://github.com/actions/runner-images/issues/14264
are fixed.
#>

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Desktop
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, IntPtr extra);
}
'@

# The runner's agent runs wsl.exe every 30 seconds, and where WSL can't run, as on the ARM64 image, that sometimes
# opens WSL's update prompt, which takes the foreground mid-test. This has Windows start a stub that does nothing in
# its place, set in both registry views since a 32-bit process reads its own.
# https://github.com/actions/runner-images/issues/14264
foreach ($software in 'SOFTWARE', 'SOFTWARE\WOW6432Node') {
    $wslOptions = "HKLM:\$software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\wsl.exe"
    New-Item $wslOptions -Force | Out-Null
    Set-ItemProperty $wslOptions Debugger "$env:SystemRoot\System32\systray.exe"
}

function Get-ForegroundProcess {
    $id = 0
    [void][Desktop]::GetWindowThreadProcessId([Desktop]::GetForegroundWindow(), [ref]$id)
    (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName
}

# The first-logon privacy screen (WWAHost) and WSL's update terminal
$leftovers = 'WWAHost', 'wsl', 'wslhost', 'WindowsTerminal', 'OpenConsole'
# Opened in their place once WWAHost ends, and closed with Esc
$flyouts = 'StartMenuExperienceHost', 'SearchHost'

function Close-Leftovers {
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Get-Process $leftovers -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        if ((Get-ForegroundProcess) -in $flyouts) {
            [Desktop]::keybd_event(0x1B, 0, 0, [IntPtr]::Zero)
            [Desktop]::keybd_event(0x1B, 0, 2, [IntPtr]::Zero)
        }
        Start-Sleep -Milliseconds 500
        $foreground = Get-ForegroundProcess
    } while ($foreground -in $leftovers + $flyouts -and (Get-Date) -lt $deadline)
    $foreground
}

# Where the privacy screen is open, as on the ARM64 image, Windows holds back the sign-in's startup apps until it
# closes. Explorer then starts the Run key's entries one at a time, the tests' among them, such as the installed app's
# start-at-login entry, which would start a copy of the app mid-test. So an entry is added last here, which only runs
# once Explorer is through those before it. RunStuffHasBeenRun marks a session whose startup apps have already begun
$explorer = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer'
$startupPending = -not (Test-Path "$explorer\SessionInfo\$((Get-Process -Id $PID).SessionId)\RunStuffHasBeenRun")
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$doneKey = 'HKCU:\Software\OpenBackupManager-CI'
function Test-StartupDone { [bool](Get-ItemProperty $doneKey -Name StartupDone -ErrorAction SilentlyContinue) }
if ($startupPending) {
    if (-not (Test-Path $runKey)) {
        New-Item $runKey | Out-Null
    }

    $reg = Join-Path $env:SystemRoot 'System32\reg.exe'
    $done = "`"$reg`" add HKCU\Software\OpenBackupManager-CI /v StartupDone /d 1 /f"
    Set-ItemProperty $runKey 'OpenBackupManager-CI' $done
}

$foreground = Close-Leftovers

if ($startupPending) {
    $started = Get-Date
    while (-not (Test-StartupDone) -and (Get-Date) -lt $started.AddMinutes(3)) {
        Start-Sleep 1
    }

    Remove-ItemProperty $runKey 'OpenBackupManager-CI'
    if (Test-StartupDone) {
        Write-Host "Windows ran the startup apps in $([int]((Get-Date) - $started).TotalSeconds) seconds"
    }
    else {
        Write-Host "::warning::Windows didn't run the startup apps, so it may yet start the tests' Run entries mid-test"
    }

    # They can open windows too
    $foreground = Close-Leftovers
}

Write-Host "In front: $foreground"

# Windows sometimes warns that it made a temporary paging file, from a dialog that can open minutes into the job and
# take the foreground mid-test, so it's closed whenever it opens until the job ends and the runner stops this
Start-Process pwsh -WindowStyle Hidden -ArgumentList '-NoProfile', '-Command',
    'while ($true) { Get-Process SystemPropertiesPerformance -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep 1 }'
