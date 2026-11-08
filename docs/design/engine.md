# Engine and command line

`obmd` is the engine: it runs in the background as the user, one for each home, and does the work. `obm` is the command line, a client that talks to it and keeps no state, as the tray app does (see [Windows app](windows-app.md)). What each command does, and how to run the engine on each OS, is in the guide's [Command line](../guide/command-line.md).

## Code map

- `OpenBackupManager.Engine`: the engine itself. `obmd` runs it, `OpenBackupManager.Engine.Tests` runs it in-process, and `obm` never loads it.
- `OpenBackupManager.Daemon`: `obmd`, which runs the engine on the generic host, plus `WindowsShutdownHandler` and `SecretsRemoval`.
- `OpenBackupManager.Cli`: `obm`, with the services (`EngineService`, `LaunchAgent`, `SystemdService`), `obm update` (`Update`, `Releases`) and `obm data remove` (`Data`).
- `OpenBackupManager.Core`, shared with the app: the home (`AppHome`), the lock (`ProcessLock`), the socket and protocol (`Protocol`), crash reports (`CrashReports`, `BootId`, `Journal`, `DiagnosticReports`), `Redaction`, `BugReport` and `RunningAs`.
- `OpenBackupManager.Cli.Tests` runs the built `obm` and `obmd` on every CI runner, as a user would.
- Packages: `tools/Pack-Tarball.ps1`, `nfpm.yaml` and the `Dockerfile`, each tested as installed by a script in `.github/scripts`.

## Two programs

- **`obmd` is a program of its own,** rather than a command of `obm`, so `obm` is only ever a client: nothing it runs starts an engine or keeps state.
- **On macOS and Linux, they're published side by side,** as the tarball has them, so `obm` finds `obmd` beside itself. On Windows, `obm` alone goes in `bin`, which installing adds to PATH.
- **Logs go to the console as well as to files,** so running it by hand shows what it's doing, and service managers such as systemd keep what it prints.

## Runs as the user

- **The engine runs as the user, with their normal rights.** That's where the tray app and `obm` look for it, and the files it writes are theirs. Backing up a whole machine or a server needs more rights, and is left to tools made for it, such as restic, Kopia and rclone.
- **`obmd` won't run as administrator when Windows has a normal token for it,** as in a terminal opened as administrator, since Windows makes an elevated process's pipe belong to Administrators, so the tray app and `obm` in a normal terminal couldn't reach it. With UAC off, as the built-in Administrator or over SSH, the admin token is the only one, so it runs.
- **No sudo on macOS and Linux,** since the engine would run as root, writing files the user can't change, and on macOS, where sudo keeps `HOME`, into the user's own folders. `obm` would look for root's engine, which is another home. Real root, as with a root login, `su -` or a container, is just another user with its own engine. The variables sudo sets can be set by hand, so this only stops a mistake, and is no security check.

## One engine for each home

- **A lock file, which frees itself if the engine crashes.** The OS closes the file, and with it the lock, when the process ends, however it ends. A file holding the engine's process ID would stay behind after a crash, and the next start would have to work out whether that ID still belongs to the engine.
- **A lock file on Windows too,** rather than a named mutex, so a short name, junction or `subst` drive for the home still leads to the same lock, where a mutex would be named from the path as written. The pipe and secret names still come from the written path, so a client using another spelling won't find the engine, but a second engine is refused.
- **The lock and the socket go in the state folder, not `$XDG_RUNTIME_DIR`.** The state folder is the same in every session, but `XDG_RUNTIME_DIR` isn't set without a login session, as with `su`, cron, some SSH setups and containers, so a lock there wouldn't stop a second engine.

## Talking to the engine

- **The boundary is the user account.** Each end only talks to the other if the OS says it runs as the same user, so other accounts can't send the engine commands or pretend to be it. It doesn't prove the engine is really `obmd`, but anything running as the user could read the same files and tokens anyway.
- **JSON-RPC 2.0 over a socket, one message to a line,** so programs not written in .NET can talk to the engine too, with System.Text.Json's source generator, so it needs no reflection.
- **`hello` comes first, and keeps its shape in every version,** as it's how two versions find out they differ. The engine refuses a client whose protocol isn't its own, saying both versions. The protocol only goes up once there are releases people use, and until then changes are made in place.
- **The engine's messages are in English.** `obm` prints them as they are, while the tray app words the ones it shows from the error's code, so they can be translated.
- **`--json` prints the engine's records,** rather than a format of its own, so there's one set of names to keep. They're a contract for scripts, changed as carefully as the protocol, which `JsonTests` pins. Errors stay on stderr, with nothing on stdout, as gh, kubectl and docker do, so a script checks the exit code. Only JSON, as `jq`, PowerShell's `ConvertFrom-Json` and `yq` cover the rest.
- **An error in `config.toml` when the engine starts stops it starting, but one made while it runs only leaves its last good settings in use.** `obm status` still exits with 0 then, as the engine is running, and a health check that failed would restart it into an engine that won't start.

## Crash reports

What's in a report, and where people see it, is in the guide's [Crash reports](../guide/files.md#crash-reports). The engine's `CrashCapture` writes them, and finds crashes .NET never sees at the next start.

- **Linux's stderr stays in the journal, rather than going to a file as on macOS,** so `systemctl --user status` still shows why the engine couldn't start. The next start reads the dead run's lines back, which were there every time in testing, as systemd only starts it again once the crash handler has finished. launchd sends stderr nowhere unless told, so the file loses nothing on macOS.

## Services on macOS and Linux

`obm service install` keeps the engine running through the OS's own service manager, as the user: a LaunchAgent on macOS and a systemd user service on Linux. Each home other than the usual folders gets its own.

- **No service on Windows,** where the tray app starts the engine at sign-in. The Run key, Windows' version of a LaunchAgent, would only add an engine at sign-in for anyone without the app, and only once someone signs in, which a server never does. A real Windows service needs admin to install, and runs as LocalSystem, or as the user with their password stored in its settings, in session 0, out of reach of their Credential Manager, OneDrive's online-only files, mapped drives and notifications. A Task Scheduler task with no stored password (S4U) has the user's identity but not their keys, so their secrets stay locked. A LocalSystem service that starts `obmd` in the user's session would work, but its exe must live in Program Files, so every update to it needs admin. Anyone who wants `obmd` as a service can wrap it with NSSM or WinSW.
- **The package installs or removes no service,** as `obm service install` is for one user and one home, and a package's scripts run as root without knowing whose. [Syncthing's](https://github.com/syncthing/syncthing/blob/v2.1.7/script/deb-post-inst.template) nudges every running instance, and [Keybase's](https://github.com/keybase/client/blob/v6.6.3/packaging/linux/post_install.sh#L8-L140) restarts each user's services through `su`, which takes over 100 lines and an opt-out. Instead, the engine removes its own service when `obmd` is removed.
- **`ServiceTests` install with the real service manager,** so they're in the Sandbox category, which CI runs.

## Updates on macOS and Linux

What people do to update is in the guide's [Updates](../guide/updates.md).

- **The engine restarts itself into an update,** within seconds, so an upgrade is in effect once it's done, rather than waiting for a sign-in. One its service started exits for the service manager to start again, and one started by hand only warns, as Chromium asks to be relaunched, since nothing would start it again. A backup never quietly stops that way.
- **Not on Windows,** where Velopack [ends whatever runs from its folder](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/apply_windows_impl.rs#L150-L159) before updating it (see [Windows app](windows-app.md)).
- **`obm update` checks the download against GitHub's own SHA-256 for it,** as Velopack checks Windows packages against its feed. GitHub [works it out](https://github.blog/changelog/2025-06-03-releases-now-expose-digests-for-release-assets/) when a file is uploaded, and this repository makes its releases [immutable](https://docs.github.com/en/code-security/supply-chain-security/understanding-your-software-supply-chain/immutable-releases), so it can't be changed afterwards. `gh attestation verify` checks more, that the project's CI built it, but needs `gh`, so it stays for anyone who wants it.
- **`obm update` leaves a .deb, .rpm or Docker install to its own package manager or image,** which an `installed-by` file beside the programs names. A .deb or .rpm needs root, which `obm` refuses.
- **Nothing checks for updates by itself on macOS and Linux,** as there's no app there to say so, and an engine that updated itself could stop partway through a backup. A script can run `obm update --check`.

## Removing data

- **Uninstalling keeps the data and secrets,** so installing again carries on where it left off, as most desktop apps and package managers do, apt's `purge` and Flatpak's `--delete-data` being separate steps. `obm data remove` is that step, on every OS, as Velopack's uninstaller can't ask: its dialog's text is fixed, and its hooks can't tell a silent uninstall from one a person started.
- **`obmd` removes the secrets,** as only it has the OS's stores, and `obm` stays a client with no platform code. It runs once the engine has stopped, holding the engine's lock, so nothing saves one meanwhile, and doesn't read `config.toml`, which may be what's broken. It looks in every store the OS has, as moving them can leave some behind.

## Builds and packages

- **Linux builds use .NET's own cross-build images,** which link against an older distro's C library (glibc). A program needs at least the glibc it was linked against, so one built on the runner's Ubuntu wouldn't start on Debian 12 or RHEL 9. .NET builds itself this way, and [supports its own builds](https://github.com/dotnet/core/blob/44927bc821d37f596e317c4316335632bd38c79b/release-notes/10.0/supported-os.md#libc) back to glibc 2.27, Ubuntu 18.04's. The images only run on x64, so check-linux tests each architecture on its own runner.
- **ICU stays a requirement on Linux,** rather than turning on .NET's invariant globalization, which would drop it. Without ICU, .NET can't normalise Unicode, which the engine will need for file names: macOS stores accented letters decomposed, and Windows and Linux composed. Desktop distros have ICU already, and the guide says how to add it to a minimal install.
- **nfpm makes the .deb and .rpm,** both from one file, with no dpkg or rpm tools, as GoReleaser does for Go programs. fpm needs Ruby, and rpmbuild for an .rpm.
- **The Docker image holds the Linux tarballs' `obm` and `obmd`,** rather than a build of its own, so it has the attested files.
- **The image is Microsoft's [chiseled](https://github.com/dotnet/dotnet-docker/blob/7a1cdd5dd426ae782d7304ab8af476855790186a/documentation/ubuntu-chiseled.md) `runtime-deps`, in its `-extra` form,** which holds only glibc, ICU, time zones, OpenSSL, certificates and the C++ runtime. With no shell or package manager there's little to patch, and as the `Dockerfile` only copies files, the Arm64 image is made without QEMU. So there's no start script switching to a `PUID` and `PGID` user, as linuxserver.io's images have, and Docker's own `user` does that.
- **The image runs as its own non-root user unless `user` picks another,** as the engine runs as you everywhere else, and files it writes in mounted folders then aren't root's.
- **No init process in the image.** `obmd` is PID 1 and stops on `SIGTERM` itself. Docker's own `init: true` adds one if a program it starts, such as rclone, ever leaves processes behind.
- **`latest` moves when a release is published, not when its tag is pushed,** as the release is still a draft then, and a NAS often updates its containers by itself.

## Built ourselves

- **Our own JSON-RPC, not StreamJsonRpc (checked Nov 2026).** StreamJsonRpc's System.Text.Json formatter is [marked as needing dynamic code](https://github.com/microsoft/vs-streamjsonrpc/blob/v2.26.150/src/StreamJsonRpc/SystemTextJsonFormatter.cs#L26), so NativeAOT builds would have to suppress the warnings, and it brings about eight packages, such as MessagePack, Nerdbank.Streams and Newtonsoft.Json, to replace about 150 lines. It also [reports a method's exception as -32000](https://github.com/microsoft/vs-streamjsonrpc/blob/v2.26.150/docfx/docs/exceptions.md) by default, which is our protocol mismatch, and has nothing like `hello` having to come first. CurlyRpc fits better on paper but was new and before 1.0. Both are worth trying again once the engine needs progress and cancellation, which StreamJsonRpc sends as LSP's `$/progress` and `$/cancelRequest`; if neither fits then, ours adds the same conventions. The transport stays ours either way, as no library has its rules: the private socket folder, refusing a link, listening only if nothing else has the name, and letting an elevated `obm` in.
- **Installing the service, the engine's lock and stopping it are ours too (checked Nov 2026).** Redpoint.ServiceControl only installs system-wide services, which need root. AutoLaunch's LaunchAgent doesn't escape its arguments or start the agent, and on Linux it writes autostart files rather than a systemd unit. The single-instance packages use a mutex and a pipe on Windows only, and none uses a lock file. The generic host's [console lifetime](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Microsoft.Extensions.Hosting/src/Internal/ConsoleLifetime.netcoreapp.cs#L17-L51) doesn't handle a console closing, and signing out and shutting down reach a desktop program as window messages it doesn't listen for, so `WindowsShutdownHandler` handles them and waits for the host.
- **Third-party notices are made by our own `tools/NoticesGen` (checked Nov 2026).** ThirdLicense, nuget-license, ThirdPartyLibraries and dotnet-project-licenses, tried on the engine, all list build-only packages such as the NativeAOT compiler, none works per runtime, most link to each licence rather than including its text, and none puts packages with the same text together.

## Accepted limits

- **On Windows, stopping is cut off after about 5 seconds.** Windows ended a test process that long after its console closed, [as documented](https://learn.microsoft.com/en-us/windows/console/handlerroutine#timeouts), and after signing out, shutting down or restarting in Windows Sandbox, even without forcing it (Nov 2026). Ctrl+C has no limit. Later steps' work has to stop within that, or be safe to cut off.
- **An engine run as administrator can only be reached as administrator.** Windows makes an elevated process's objects belong to Administrators rather than the user. `obmd` only runs as administrator when Windows has no normal token for it, so this only comes up when an admin starts it over SSH and then uses the tray app at the desktop.
- **A Unix socket's path has a limit,** 103 bytes on [macOS](https://github.com/apple-oss-distributions/xnu/blob/xnu-11215.1.10/bsd/sys/un.h#L79) and 107 on [Linux](https://github.com/torvalds/linux/blob/v6.12/include/uapi/linux/un.h#L7), as the 104 and 108 they hold include the byte that ends it, so a `--home` path over about 87 bytes on macOS or 91 on Linux can't have an engine. It says so and suggests a shorter one.
- **macOS won't run them from a browser download,** which it quarantines, since they aren't signed by Apple. The guide shows how to remove the quarantine, or to download with `curl`, which doesn't add it.
- **A crash report from macOS gives addresses, not names, for `obmd`'s own frames in a release,** as releases ship without the `.dSYM` files ReportCrash needs to name them. The signal, what sent it and the system frames still say how it ended, and a FailFast's own stack, in `obmd-stderr.log`, names the methods.
- **With two copies installed, `obm update` can update the one the engine isn't running,** such as a tarball's beside a package's. Then nothing restarts the engine, so `obm update` waits 30 seconds and says it didn't come back.
- **On macOS, launchd keeps a removed engine's job loaded until you sign out,** as unloading it would stop the engine as it stops. It's stopped with 0, so launchd doesn't start it again, and with its file gone, it isn't loaded at the next sign-in.
- **An engine started by hand runs the old version after an upgrade until it's restarted,** as nothing would start it again. The log and `obm status` say so.
- **A service runs `obmd` from where it was when it was installed,** so after moving them, `obm service install` has to be run again. Package managers that put each version in its own folder, as Homebrew does, will need theirs to point at a path that stays the same.
- **The service keeps the `XDG_` folders `obm` saw when it was installed.** It doesn't read the shell's profile, so if they change later, `obm` looks for the engine in the new folders while it runs in the old ones. Running `obm service install` again catches it up.
- **An engine that fails as it starts is started again every 10 seconds on macOS,** which is [launchd's least](https://github.com/apple-oss-distributions/launchd/blob/launchd-842.92.1/man/launchd.plist.5#L234-L237), for as long as it fails. systemd gives up after 5 tries in 10 seconds, [its default](https://www.freedesktop.org/software/systemd/man/258/systemd-system.conf.html#DefaultStartLimitIntervalSec=), and `systemctl --user status` says so.
- **A distro that moves to a newer ICU can't install the .deb** until that version is added to its list, as with [Microsoft's own packages](https://github.com/dotnet/runtime/blob/v10.0.13/src/installer/pkg/sfx/installers/dotnet-runtime-deps/dotnet-runtime-deps-debian.proj#L10-L11): apt refuses it, though the tarball still works. CI checks the newest Fedora, which tends to move first, and the newest Ubuntu LTS.
- **The Docker image is for Linux hosts.** Docker Desktop on Windows [only reports changes](https://docs.docker.com/desktop/features/wsl/best-practices/) to files kept in its own Linux, and on macOS isn't documented to, so the guide says not to use it there.
- **Finding no engine takes a moment.** A client waits up to its timeout, 2 seconds for `obm status`, since an engine may be starting, and a socket left behind by a crash looks the same as one that isn't ready yet.

## Checking by hand

CI checks stopping with `SIGINT` and `SIGTERM` on macOS and Linux. On Windows, a test would have to give up its own console to send Ctrl+C or close another's, or sign out, so there, run `obmd --home <a test folder>` and check `Engine stopped` is logged after each of these, as was checked in Windows Sandbox:

- pressing Ctrl+C
- closing the console window
- signing out
