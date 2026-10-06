<#
.SYNOPSIS
Closes the windows GitHub's Windows runners leave holding the foreground, so the UI tests' app can take it.
Remove once https://github.com/actions/runner-images/issues/14069 is fixed.
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

function Get-ForegroundProcess {
    $id = 0
    [void][Desktop]::GetWindowThreadProcessId([Desktop]::GetForegroundWindow(), [ref]$id)
    (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName
}

# The first-logon privacy screen (WWAHost) and WSL's update terminal
$leftovers = 'WWAHost', 'wsl', 'wslhost', 'WindowsTerminal', 'OpenConsole'
# Opened in their place once WWAHost ends, and closed with Esc
$flyouts = 'StartMenuExperienceHost', 'SearchHost'
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

Write-Host "In front: $foreground"
