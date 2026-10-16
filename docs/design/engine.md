# Engine and command line

`obm`, the command line, and the engine that runs in the background as `obm daemon`. The engine only runs for now, and later steps give it work to do.

## How it works

- `src/OpenBackupManager.Cli` builds `obm` with System.CommandLine. It runs on Windows, macOS and Linux, and `OpenBackupManager.Cli.Tests` runs the built `obm` on every CI runner, as a user would.
- `obm daemon` runs the engine in the foreground with no UI, on the generic host, until Ctrl+C, the console closing, the user signing out, or a service manager stopping it. It logs to `engine-<date>.log` in the logs folder, rotated like the app's logs, and prints the same lines.
- `--home` or the `OBM_HOME` environment variable picks a home folder, as for the app, through Core's `AppHome`. Without one, Windows uses the same home as the tray app: `%LocalAppData%\OpenBackupManager`, or `OpenBackupManager-Dev` for a build that wasn't installed, which it finds out by asking Velopack, since `obm` is installed beside the app. macOS and Linux use their usual folders (see [Configuration and files](config.md)).
- One engine runs for each home, held by Core's `ProcessLock`. On Windows it's a named mutex named from the home's key. On macOS and Linux it's an `flock` on `engine.lock` in the state folder. A second `obm daemon` for the same home says the engine is already running and exits with 1.

## Why

- **The lock frees itself if the engine crashes.** The OS releases a mutex or `flock` when the process holding it ends, however it ends, so the next start never finds a stale lock. A file holding the engine's process ID would stay behind after a crash, and the next start would have to work out whether that ID still belongs to the engine or has been reused by another process.
- **A named mutex on Windows,** which leaves no file behind. It's only closed at the end, not released, since only the thread that took it can release it.
- **`flock` on macOS and Linux,** as Unix daemons lock. .NET takes an exclusive `flock` when a file is opened with `FileShare.None`.
- **The state folder, not `$XDG_RUNTIME_DIR`,** for the lock on Linux. The state folder is the same in every session, but `XDG_RUNTIME_DIR` isn't set without a login session, as with `su`, cron, some SSH setups and containers. A lock there would be in a different place for those, so it wouldn't stop a second engine.
- **The generic host's console lifetime stops it, not System.CommandLine's Ctrl+C handling,** since the host also stops on `SIGTERM`, so System.CommandLine's handling is turned off.
- **`WindowsShutdownHandler` stops it when its console closes, or the user signs out or shuts down.** Windows ends the process as soon as each of these has been handled, which the host does straight away, so the engine never got to stop. It waits for the host to stop instead, for up to 5 seconds: Windows' own limit when the console closes, and ours for signing out and shutting down, where Windows would otherwise move on to its screen of apps that are preventing it. The console's own sign-out and shutdown events never arrive, because .NET loads `user32.dll`, and Windows then only tells a process through a window, so those come through `SystemEvents`' hidden window.
- **Logs on the console as well as in files,** so running it by hand shows what it's doing, and service managers such as systemd keep what it prints.

## Accepted limits

- **On Windows, stopping is cut off after 5 seconds.** That's Windows' limit when the console closes, and ours when the user signs out or shuts down, so a slow stop never leaves them waiting on Windows' screen of apps preventing it. Later steps' work has to stop within that, or be safe to cut off.
- **The Windows mutex is per session.** The same user signed in twice at once, as Windows Server allows, could run two engines for one home.

## Checking by hand

CI checks stopping with `SIGINT` and `SIGTERM` on macOS and Linux. On Windows, a test would have to give up its own console to send Ctrl+C or close another's, or sign out, so there, run `obm daemon --home <a test folder>` and check `Engine stopped` is logged after each of these, as was checked in Windows Sandbox:

- pressing Ctrl+C
- closing the console window
- signing out
