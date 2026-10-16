<#
.SYNOPSIS
Checks obm and obmd run as the user with their normal rights. It makes a throwaway admin account on Windows and uses
sudo on macOS and Linux, so it only runs on CI, after the build.

On Windows, obmd refuses to start as administrator when Windows has a normal token it could use instead, and runs
when the admin token is the only one, as over SSH. On macOS and Linux, obm and obmd refuse sudo, but run as real root.

Each check adds a PASS or FAIL line to the results, which are printed at the end.

.PARAMETER AsLimitedUser
Runs the throwaway account's checks. Test-Windows starts this script again with it, as that account.
#>
param([switch]$AsLimitedUser)

$ErrorActionPreference = 'Stop'

$dir = if ($IsWindows) { 'C:\running-as' } else { "$env:RUNNER_TEMP/running-as" }
$exe = if ($IsWindows) { '.exe' } else { '' }
$obm = Join-Path $dir 'bin' "obm$exe"
$obmd = Join-Path $dir 'bin' "obmd$exe"
$results = Join-Path $dir 'results.txt'
$limitedResults = Join-Path $dir 'limited-results.txt'

# Helpers

function Add-Result([string]$name, [bool]$passed, [string]$details) {
    $file = if ($AsLimitedUser) { $limitedResults } else { $results }
    Add-Content $file ($passed ? "PASS $name" : "FAIL $name`n$details")
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

    # Not Start-Process -Wait, which would wait for any engine it left running too
    $script = Join-Path $dir (Split-Path $PSCommandPath -Leaf)
    $limited = Start-Process pwsh -Credential ([pscredential]::new($user, $password)) -LoadUserProfile -WorkingDirectory $dir -ArgumentList '-NoProfile', '-File', $script, '-AsLimitedUser' -PassThru
    if (-not $limited.WaitForExit(180000)) {
        Add-Result 'the throwaway account''s checks' $false 'They were still running after 3 minutes'
    }
    elseif (-not (Test-Path $limitedResults)) {
        Add-Result 'the throwaway account''s checks' $false 'They never ran'
    }
    if (Test-Path $limitedResults) {
        Add-Content $results (Get-Content $limitedResults)
    }

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
    # sudo clears DOTNET_ROOT, which obm and obmd need to find .NET where setup-dotnet put it
    $sudo = '-n', 'env', "DOTNET_ROOT=$env:DOTNET_ROOT"
    Test-Tool 'obmd refuses sudo' 1 'without sudo' sudo ($sudo + $obmd, '--home', "$dir/home-a")
    Test-Tool 'obm refuses sudo' 1 'without sudo' sudo ($sudo + $obm, 'status', '--home', "$dir/home-a")
    Test-Tool 'obm --version still works under sudo' 0 '' sudo ($sudo + $obm, '--version')

    # Real root, as with a root login or su -, has none of sudo's variables, so sudo without them stands in for it
    $root = '-n', 'env', '-u', 'SUDO_UID', '-u', 'SUDO_GID', '-u', 'SUDO_USER', '-u', 'SUDO_COMMAND', "DOTNET_ROOT=$env:DOTNET_ROOT"
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

if ($env:GITHUB_ACTIONS -ne 'true') {
    throw 'This makes an admin account or uses sudo, so it only runs on CI'
}

New-Item (Join-Path $dir 'bin') -ItemType Directory -Force | Out-Null
$source = Join-Path $PSScriptRoot '../../src'
Copy-Item "$source/OpenBackupManager.Cli/bin/Debug/net10.0/*", "$source/OpenBackupManager.Daemon/bin/Debug/net10.0/*" (Join-Path $dir 'bin') -Recurse -Force

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
