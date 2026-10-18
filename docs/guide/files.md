# Files and folders

Where Open Backup Manager keeps its files, and what's in them.

## Your data

Everything the app keeps about this device, such as its settings and logs, is its data. On Windows it all goes in one folder, the data folder, `%LocalAppData%\OpenBackupManager\`. On macOS and Linux each kind of file goes in the usual folder for it:

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Settings | `%LocalAppData%\OpenBackupManager\` | `~/.config/openbackupmanager/` | `~/.config/openbackupmanager/` |
| Logs | `%LocalAppData%\OpenBackupManager\logs\` | `~/Library/Logs/OpenBackupManager/` | `~/.local/state/openbackupmanager/logs/` |
| Crash reports | `%LocalAppData%\OpenBackupManager\crashes\` | `~/Library/Application Support/OpenBackupManager/crashes/` | `~/.local/state/openbackupmanager/crashes/` |

- **Settings:** `config.toml`, this device's settings, and `config.toml.bak`, the version before the app last changed it. See [Settings file](config.md).
- **Logs:** a file for each day, described below.
- **Crash reports:** what went wrong when the app or its engine closed unexpectedly, described below.

The Windows app also keeps `last-version` in its data folder: the version that last ran, so it can say when it's been updated.

On macOS and Linux, the engine also keeps `engine.lock` and `run/engine.sock` in its own folder: `~/Library/Application Support/OpenBackupManager/` on macOS and `~/.local/state/openbackupmanager/` on Linux. They make sure only one engine runs and let the app talk to it, so leave them alone.

On Linux, `XDG_CONFIG_HOME` and `XDG_STATE_HOME` move these folders as they do for other apps. If the app was started with `--home <folder>`, or the `OBM_HOME` environment variable is set, everything goes in that folder instead.

## Logs

The app writes what it does to a log: starting up, checking for and installing updates, and any problems with the settings file. If something goes wrong, the log is the first place to look, and it's worth attaching to a bug report.

- There's a file for each day, such as `app-20261014.log`. A day that writes more than 10 MB carries on in `app-20261014_001.log`, and so on.
- The engine, `obmd`, has its own, such as `engine-20261014.log`.
- Only the newest 7 files of each are kept, which is usually about a week.
- Passwords and tokens are never written to the logs, so they're safe to share.

## Crash reports

If the Windows app or the engine, `obmd`, closes unexpectedly, it writes a report to the crash reports folder: what went wrong, its version, the OS version and the end of its log. Some crashes stop it before it can write anything, so on Windows, the next time it starts, it looks for what Windows recorded about them in Event Viewer and writes the report then. The Windows app shows each report once, which you can copy into a bug report: its own the next time it starts, and the engine's once it has started the engine again. Only the newest 10 are kept.

Windows also starts the app again after a crash, as long as it had been running for at least a minute, and the app starts the engine again whenever it stops.

## The app itself

On Windows the app is installed in `%LocalAppData%\OpenBackupManager.App\`, along with any update it has downloaded. Updates replace what's there, so don't keep anything of your own in it.

## Uninstalling

Uninstalling removes the app, its Start menu shortcut, its notifications and its start at login, but leaves your data, so installing it again picks up where you left off. To remove everything, delete the data folder too.
