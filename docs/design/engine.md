# Engine and command line

`obm`, the command line, and `obmd`, the engine that runs in the background. The engine only runs and answers `obm status` for now, and later steps give it work to do.

## How it works

- `src/OpenBackupManager.Cli` builds `obm` and `src/OpenBackupManager.Daemon` builds `obmd`, both with System.CommandLine. They run on Windows, macOS and Linux, and `OpenBackupManager.Cli.Tests` runs the built `obm` and `obmd` on every CI runner, as a user would.
- The engine itself is the `OpenBackupManager.Engine` library, which `obmd` runs and `OpenBackupManager.Engine.Tests` runs in-process. `obm` is only a client, which never loads it, and keeps no state of its own.
- `obmd` runs the engine in the foreground with no UI, on the generic host, until Ctrl+C, the console closing, the user signing out, or a service manager stopping it. It logs to `engine-<date>.log` in the logs folder, rotated like the app's logs, and prints the same lines.
- `--home` or the `OBM_HOME` environment variable picks a home folder, as for the app, through Core's `AppHome`, the same for `obm` and `obmd`. Without one, Windows uses the same home as the tray app: `%LocalAppData%\OpenBackupManager`, or `OpenBackupManager-Dev` for a build that wasn't installed, which they find out by asking Velopack, since they're installed beside the app. macOS and Linux use their usual folders (see [Configuration and files](config.md)).
- One engine runs for each home, held by Core's `ProcessLock`. On Windows it's a global named mutex named from the home's key, so it covers every session, including SSH. On macOS and Linux it's an `flock` on `engine.lock` in the state folder. A second `obmd` for the same home says the engine is already running and exits with 1.
- The engine listens on a socket for the tray app and `obm`: a named pipe on Windows, named from the home's key, and a Unix domain socket at `run/engine.sock` in the state folder on macOS and Linux. Core's `EngineSocket` says where, and Core's `EngineClient` connects, for both the tray app and `obm`.
- They talk JSON-RPC 2.0, one message to a line, with System.Text.Json's source generator. A client sends `hello` with its protocol and version first, and the engine refuses one whose protocol isn't its own, saying both versions, and closes the connection. `status` gives the engine's version, process ID, start time and folders, which `obm status` shows. Without an engine, `obm status` says so and exits with 1.

## Why

- **The lock frees itself if the engine crashes.** The OS releases a mutex or `flock` when the process holding it ends, however it ends, so the next start never finds a stale lock. A file holding the engine's process ID would stay behind after a crash, and the next start would have to work out whether that ID still belongs to the engine or has been reused by another process.
- **A named mutex on Windows,** which leaves no file behind. It's only closed at the end, not released, since only the thread that took it can release it. It's global rather than per session, so an engine started over SSH while one runs at the desktop says it's already running. One the process can't open, because an elevated engine or another user made it, counts as held.
- **`flock` on macOS and Linux,** as Unix daemons lock. .NET takes an exclusive `flock` when a file is opened with `FileShare.None`.
- **The state folder, not `$XDG_RUNTIME_DIR`,** for the lock on Linux. The state folder is the same in every session, but `XDG_RUNTIME_DIR` isn't set without a login session, as with `su`, cron, some SSH setups and containers. A lock there would be in a different place for those, so it wouldn't stop a second engine.
- **The boundary is the user account.** Each end only talks to the other if the OS says it runs as the same user, so other accounts can't send the engine commands or pretend to be it. It doesn't prove the engine is really `obmd`, but anything running as the user could read the same files and tokens anyway. `hello` only checks versions. Both ends get this from `PipeOptions.CurrentUserOnly`, plus the steps below.
- **On Windows,** the pipe only lets its owner open it, and a client asks Windows who owns it before sending anything. The client does that itself because .NET's check expects Administrators as the owner when the client is elevated, so `obm` in an admin terminal couldn't reach an engine that isn't. It accepts the user, or Administrators when it's elevated too.
- **On macOS and Linux,** each end asks the OS which user is at the other end. The socket file's permissions depend on the umask, so the engine keeps it in a `run` folder only the user can open (`0700`), and won't use one that's a link or someone else's.
- **The engine only listens if nothing else has the name,** through `PipeOptions.FirstPipeInstance`. Without it, .NET on macOS and Linux deletes any socket already at the path and takes it over, even a running engine's. As the engine holds its lock, any socket already there was left by one that crashed, so it deletes that first.
- **The socket sits beside the lock, in the state folder,** for the same reason as the lock. It also means one home's engine is always at the same path.
- **`obmd` is a program of its own,** rather than a command of `obm`, so `obm` is only ever a client: nothing it runs starts an engine or keeps state.
- **JSON-RPC over a socket,** so programs not written in .NET can talk to the engine too, and the source generator, so it needs no reflection.
- **The generic host's console lifetime stops it, not System.CommandLine's Ctrl+C handling,** since the host also stops on `SIGTERM`, so System.CommandLine's handling is turned off.
- **`WindowsShutdownHandler` stops it when its console closes, or the user signs out or shuts down.** Windows ends the process as soon as each of these has been handled, which the host does straight away, so the engine never got to stop. It waits for the host to stop instead, for up to 5 seconds: Windows' own limit when the console closes, and ours for signing out and shutting down, where Windows would otherwise move on to its screen of apps that are preventing it. The console's own sign-out and shutdown events never arrive, because .NET loads `user32.dll`, and Windows then only tells a process through a window, so those come through `SystemEvents`' hidden window.
- **Logs on the console as well as in files,** so running it by hand shows what it's doing, and service managers such as systemd keep what it prints.

## Accepted limits

- **On Windows, stopping is cut off after 5 seconds.** That's Windows' limit when the console closes, and ours when the user signs out or shuts down, so a slow stop never leaves them waiting on Windows' screen of apps preventing it. Later steps' work has to stop within that, or be safe to cut off.
- **An engine run as administrator can only be reached as administrator.** Windows makes an elevated process's objects belong to the Administrators group rather than the user, so a client that isn't elevated is refused.
- **A Unix socket's path has a limit,** 103 bytes on macOS and 107 on Linux, in UTF-8, so a character outside ASCII takes 2 to 4 of them. It covers the whole socket path, and `/run/engine.sock` takes 16, so a `--home` path over about 87 bytes on macOS or 91 on Linux can't have an engine. It says so and suggests a shorter one.
- **Finding no engine takes a moment.** A client waits up to its timeout, 2 seconds for `obm status`, since an engine may be starting, and a socket left behind by a crash looks the same as one that isn't ready yet.

## Checking by hand

CI checks stopping with `SIGINT` and `SIGTERM` on macOS and Linux. On Windows, a test would have to give up its own console to send Ctrl+C or close another's, or sign out, so there, run `obmd --home <a test folder>` and check `Engine stopped` is logged after each of these, as was checked in Windows Sandbox:

- pressing Ctrl+C
- closing the console window
- signing out
