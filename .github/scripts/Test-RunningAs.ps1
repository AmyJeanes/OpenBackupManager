<#
.SYNOPSIS
Checks obm, obmd and the tray app run as the user with their normal rights. It makes a throwaway admin account on
Windows and uses sudo on macOS and Linux, so it only runs on CI, on what tools/Publish-App.ps1 published to the folder
in OBM_TEST_PUBLISHED.

On Windows, obmd refuses to start as administrator when Windows has a normal token it could use instead, and runs
when the admin token is the only one, as over SSH. The tray app starts again without admin rights, or explains when it
can't. On macOS and Linux, obm and obmd refuse sudo, but run as real root.

Each check adds a PASS or FAIL line to the results, which are printed at the end.

.PARAMETER AsLimitedUser
Runs the throwaway account's checks. Test-Windows starts this script again with it, as that account.

.PARAMETER AsAdministrator
Runs the tray app's check. Test-AsLimitedUser starts this script again with it, as administrator.
#>
param([switch]$AsLimitedUser, [switch]$AsAdministrator)

$ErrorActionPreference = 'Stop'

$dir = if ($IsWindows) { 'C:\running-as' } else { "$env:RUNNER_TEMP/running-as" }
$exe = if ($IsWindows) { '.exe' } else { '' }
$obm = if ($IsWindows) { Join-Path $dir 'bin' 'obm.exe' } else { Join-Path $dir 'obm' }
$obmd = Join-Path $dir "obmd$exe"
$trayApp = Join-Path $dir 'OpenBackupManager.App.Windows.exe'
$results = Join-Path $dir 'results.txt'
$limitedResults = Join-Path $dir 'limited-results.txt'
$administratorResults = Join-Path $dir 'administrator-results.txt'

# Helpers

function Add-Result([string]$name, [bool]$passed, [string]$details) {
    $file = if ($AsLimitedUser) { $limitedResults } elseif ($AsAdministrator) { $administratorResults } else { $results }
    Add-Content $file ($passed ? "PASS $name" : "FAIL $name`n$details")
}

# Adds the results of this script started again, once it's finished. Not Start-Process -Wait, which would wait for any
# engine it left running too
function Add-ResultsOf([string]$name, [Diagnostics.Process]$process, [string]$file) {
    if (-not $process.WaitForExit(180000)) {
        Add-Result $name $false 'They were still running after 3 minutes'
    }
    elseif (-not (Test-Path $file)) {
        Add-Result $name $false 'They never ran'
    }
    if (Test-Path $file) {
        Get-Content $file | ForEach-Object { Add-Content ($AsLimitedUser ? $limitedResults : $results) $_ }
    }
}

# Runs it to the end, or for 30 seconds at most
function Invoke-Tool([string]$file, [string[]]$arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($file)
    $arguments | ForEach-Object { $start.ArgumentList.Add($_) }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::Start($start)
    $output = $process.StandardOutput.ReadToEndAsync()
    $errors = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        return @{ ExitCode = -1; Output = 'It was still running after 30 seconds' }
    }

    @{ ExitCode = $process.ExitCode; Output = $output.Result + $errors.Result }
}

# Passes if it exits with that code and prints that text
function Test-Tool([string]$name, [int]$exitCode, [string]$text, [string]$file, [string[]]$arguments) {
    $run = Invoke-Tool $file $arguments
    Add-Result $name ($run.ExitCode -eq $exitCode -and $run.Output.Contains($text)) "exit $($run.ExitCode): $($run.Output)"
}

# The same, as administrator. RunAs doesn't ask on the runners, and can't return the output, so it goes through a file
function Test-Elevated([string]$name, [int]$exitCode, [string]$text, [string]$file, [string[]]$arguments) {
    $output = Join-Path $dir "$($name -replace '\W', '-').txt"
    $command = "& '$file' $($arguments -join ' ') *>&1 | Out-String | Set-Content '$output'; Add-Content '$output' ('exit ' + `$LASTEXITCODE)"
    Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile', '-Command', $command -Wait
    $printed = Get-Content $output -Raw
    Add-Result $name ($printed.Contains("exit $exitCode") -and $printed.Contains($text)) $printed
}

# Starts it in the background and passes once the engine says it started. Returns the process, to stop it with
function Start-Engine([string]$name, [string]$file, [string[]]$arguments) {
    $log = Join-Path $dir "$($name -replace '\W', '-').txt"
    $process = Start-Process $file -ArgumentList $arguments -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $printed = "$(Get-Content $log -Raw -ErrorAction Ignore)$(Get-Content "$log.err" -Raw -ErrorAction Ignore)"
        $started = $printed.Contains('started, process')
    } until ($started -or $process.HasExited -or (Get-Date) -gt $deadline)

    Add-Result $name $started $printed
    $process
}

# Windows

function Test-Windows {
    Copy-Item $PSCommandPath $dir
    icacls $dir /grant 'Everyone:(OI)(CI)F' | Out-Null

    # The runner is the built-in Administrator, which only has an admin token. This account has UAC on, so its
    # sign-in gets a normal token, as yours does
    $user = 'obmtest'
    $password = ConvertTo-SecureString ('Pa1!' + [guid]::NewGuid().ToString('N')) -AsPlainText -Force
    New-LocalUser $user -Password $password -PasswordNeverExpires | Out-Null
    Add-LocalGroupMember Administrators $user
    Start-Service seclogon # Start-Process -Credential needs it

    $script = Join-Path $dir (Split-Path $PSCommandPath -Leaf)
    $limited = Start-Process pwsh -Credential ([pscredential]::new($user, $password)) -LoadUserProfile -WorkingDirectory $dir -ArgumentList '-NoProfile', '-File', $script, '-AsLimitedUser' -PassThru
    Add-ResultsOf 'the throwaway account''s checks' $limited $limitedResults

    if (Get-Service sshd -ErrorAction Ignore) {
        Test-OverSsh $user
    }
    else {
        Write-Host 'No OpenSSH Server on this runner, so the SSH checks are left out'
    }

    Remove-LocalUser $user
}

function Test-AsLimitedUser {
    $engine = Start-Engine 'obmd runs with the normal token' $obmd '--home', "$dir\home-a"
    Test-Tool 'obm reaches it with the normal token' 0 'The engine is running' $obm 'status', '--home', "$dir\home-a"
    Test-Elevated 'obm reaches it as administrator' 0 'The engine is running' $obm 'status', '--home', "$dir\home-a"
    Stop-Process -Id $engine.Id

    Test-Elevated 'obmd refuses to run as administrator' 1 'running as administrator' $obmd '--home', "$dir\home-b"

    # As administrator too, so it can read the tray app's message
    $elevated = Start-Process pwsh -Verb RunAs -ArgumentList '-NoProfile', '-File', $PSCommandPath, '-AsAdministrator' -PassThru
    Add-ResultsOf 'the tray app''s check' $elevated $administratorResults
}

# A UI Automation condition, such as the elements of a process, or with a name
function New-Condition([string]$property, $value) {
    [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::"${property}Property", $value)
}

# The tray app starts again through the desktop's Explorer, which here runs as the runner's account rather than this
# one, so it explains instead
function Test-TrayAppAsAdministrator {
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $app = Start-Process $trayApp -ArgumentList '--home', "$dir\home-e" -PassThru
    $ofApp = New-Condition ProcessId $app.Id
    $deadline = (Get-Date).AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 200
        $message = [Windows.Automation.AutomationElement]::RootElement.FindFirst('Children', $ofApp)
    } until ($message -or $app.HasExited -or (Get-Date) -gt $deadline)

    # PowerShell's UI Automation can't load its support for Windows' own controls, so the message's text and button
    # only have their names and windows. The button is pressed by sending it what a click does
    $text = $message ? ($message.FindAll('Descendants', [Windows.Automation.Condition]::TrueCondition).Current.Name -join "`n") : $null
    Add-Result 'the tray app explains it can''t run as administrator' ("$text".Contains('running as administrator')) "Its message: $text"
    if ($message) {
        $ok = $message.FindFirst('Descendants', (New-Condition Name 'OK')).Current.NativeWindowHandle
        Add-Type -Namespace Win32 -Name Window -MemberDefinition '[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr w, IntPtr l);'
        [Win32.Window]::PostMessage($ok, 0xF5, 0, 0) | Out-Null # BM_CLICK
    }

    $exited = $app.WaitForExit(30000)
    Add-Result 'the tray app exits after explaining' $exited 'It was still running after 30 seconds'
    if (-not $exited) {
        $app.Kill()
    }
    $left = Get-Process 'OpenBackupManager.App.Windows', 'obmd' -ErrorAction Ignore
    Add-Result 'the tray app started no other copy and no engine' (-not $left) ($left | Format-Table Id, Name, StartTime | Out-String)
}

# Over SSH, an admin only gets an admin token. Only the x64 runner has OpenSSH Server
function Test-OverSsh([string]$user) {
    Start-Service sshd
    $key = Join-Path $dir 'key'
    ssh-keygen -t ed25519 -N '' -f $key -q
    # Lets any admin sign in with the key
    $authorized = "$env:ProgramData\ssh\administrators_authorized_keys"
    Copy-Item "$key.pub" $authorized
    icacls $authorized /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F' | Out-Null
    $ssh = '-i', $key, '-o', 'StrictHostKeyChecking=no', '-o', 'BatchMode=yes', '-o', 'LogLevel=ERROR'

    $engine = Start-Engine 'obmd runs at the desktop' $obmd '--home', "$dir\home-c"
    Test-Tool 'a second obmd over SSH finds it running' 1 'already running' ssh ($ssh + "$env:USERNAME@localhost", "$obmd --home $dir\home-c")
    Stop-Process -Id $engine.Id

    $session = Start-Engine 'obmd runs over SSH as an admin with UAC on' ssh ($ssh + "$user@localhost", "$obmd --home $dir\home-d")
    # Stop-Process -Name throws for a process in the SSH session
    taskkill /f /im obmd.exe | Out-Null
    $session.WaitForExit()
}

# macOS and Linux

function Test-MacAndLinux {
    $sudo = @('-n')
    Test-Tool 'obmd refuses sudo' 1 'without sudo' sudo ($sudo + $obmd, '--home', "$dir/home-a")
    Test-Tool 'obm refuses sudo' 1 'without sudo' sudo ($sudo + $obm, 'status', '--home', "$dir/home-a")
    Test-Tool 'obm --version still works under sudo' 0 '' sudo ($sudo + $obm, '--version')

    # Real root, as with a root login or su -, has none of sudo's variables, so sudo without them stands in for it
    $root = '-n', 'env', '-u', 'SUDO_UID', '-u', 'SUDO_GID', '-u', 'SUDO_USER', '-u', 'SUDO_COMMAND'
    $engine = Start-Engine 'obmd runs as real root' sudo ($root + $obmd, '--home', "$dir/home-b")
    $status = Invoke-Tool sudo ($root + $obm, 'status', '--home', "$dir/home-b")
    Add-Result 'obm reaches it as real root' ($status.ExitCode -eq 0) "exit $($status.ExitCode): $($status.Output)"
    if ($status.Output -match 'Process: (\d+)') {
        sudo -n kill $Matches[1]
    }
    $engine.WaitForExit(30000) | Out-Null
}

# Running them

if ($AsLimitedUser) {
    try {
        Test-AsLimitedUser
    }
    catch {
        Add-Result 'the throwaway account''s checks ran to the end' $false $_
    }
    exit
}

if ($AsAdministrator) {
    try {
        Test-TrayAppAsAdministrator
    }
    catch {
        Add-Result 'the tray app''s check ran to the end' $false $_
    }
    exit
}

if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'This makes an admin account or uses sudo, so it only runs on CI'
}

New-Item $dir -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $env:OBM_TEST_PUBLISHED '*') $dir -Recurse -Force

try {
    if ($IsWindows) {
        Test-Windows
    }
    else {
        Test-MacAndLinux
    }
}
catch {
    Add-Result 'the checks ran to the end' $false $_
}

$lines = @(Get-Content $results -ErrorAction Ignore)
$lines | Write-Host
$failed = $lines.Count -eq 0 -or ($lines | Where-Object { $_.StartsWith('FAIL') })
exit ($failed ? 1 : 0)
