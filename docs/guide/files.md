# Files and folders

Where Open Backup Manager keeps its files, and what's in them.

## Your data

Everything Open Backup Manager keeps about this device, such as its logs, is its data.

=== "Windows"

    It all goes in one folder, the data folder, `%LocalAppData%\OpenBackupManager\`.

    | | Folder |
    | --- | --- |
    | Settings | `%LocalAppData%\OpenBackupManager\` |
    | Logs | `%LocalAppData%\OpenBackupManager\logs\` |
    | Crash reports | `%LocalAppData%\OpenBackupManager\crashes\` |

    The settings are `config.toml`, and `config.toml.bak`, the version before the app last changed it. See [Settings](settings.md). The app also keeps `last-version` there: the version that last ran, so it can say when it's been updated.

=== "macOS"

    Each kind of file goes in the usual folder for it.

    | | Folder |
    | --- | --- |
    | Logs | `~/Library/Logs/OpenBackupManager/` |
    | Crash reports | `~/Library/Application Support/OpenBackupManager/crashes/` |

    The engine also keeps `engine.lock` and `run/engine.sock` in its own folder, `~/Library/Application Support/OpenBackupManager/`. They make sure only one engine runs and let `obm` talk to it, so leave them alone.

=== "Linux"

    Each kind of file goes in the usual folder for it.

    | | Folder |
    | --- | --- |
    | Logs | `~/.local/state/openbackupmanager/logs/` |
    | Crash reports | `~/.local/state/openbackupmanager/crashes/` |

    The engine also keeps `engine.lock` and `run/engine.sock` in its own folder, `~/.local/state/openbackupmanager/`. They make sure only one engine runs and let `obm` talk to it, so leave them alone.

    `XDG_STATE_HOME` moves these folders as it does for other apps.

If `--home <folder>` was given, or the `OBM_HOME` environment variable is set, everything goes in that folder instead.

## Logs

Open Backup Manager writes what it does to logs. If something goes wrong, they're the first place to look, and they're worth attaching to a bug report.

=== "Windows"

    The app and the engine each have their own. The app's, such as `app-20261014.log`, has it starting up, checking for and installing updates, and any problems with the settings file. The engine's, such as `engine-20261014.log`, has what `obmd` does.

=== "macOS"

    The engine, `obmd`, writes one, such as `engine-20261014.log`.

=== "Linux"

    The engine, `obmd`, writes one, such as `engine-20261014.log`.

- There's a file for each day. A day that writes more than 10 MB carries on in a file such as `engine-20261014_001.log`, and so on.
- Only the newest 7 files of each are kept, which is usually about a week.
- `obm` doesn't keep a log: what it has to say, it prints in the terminal.
- Passwords and tokens are never written to the logs, so they're safe to share.

## Crash reports

If the engine closes unexpectedly, it writes a report to the crash reports folder: what went wrong, its version, the OS version and the end of its log. Only the newest 10 are kept.

=== "Windows"

    The app writes one too when it closes unexpectedly. Some crashes stop either of them before they can write anything, so the next time it starts, it looks for what Windows recorded about them in Event Viewer and writes the report then. The app shows each report once, which you can copy into a bug report: its own the next time it starts, and the engine's once it has started the engine again.

    Windows also starts the app again after a crash, as long as it had been running for at least a minute, and the app starts the engine again whenever it stops.

=== "macOS"

    Some crashes stop the engine before it can write anything. Then the next time it starts, it writes a warning in its log instead. Nothing shows the reports or starts the engine again yet, so if it stops by itself, look in the crash reports folder.

=== "Linux"

    Some crashes stop the engine before it can write anything. Then the next time it starts, it writes a warning in its log instead. Nothing shows the reports or starts the engine again yet, so if it stops by itself, look in the crash reports folder.

## The app itself

=== "Windows"

    The app is installed in `%LocalAppData%\OpenBackupManager.App\`, along with any update it has downloaded. Updates replace what's there, so don't keep anything of your own in it.

=== "macOS"

    There's no app for macOS yet.

=== "Linux"

    There's no app for Linux yet.

## Uninstalling

=== "Windows"

    Uninstalling removes the app, its Start menu shortcut, its notifications and its start at login, but leaves your data, so installing it again picks up where you left off. To remove everything, delete the data folder too.

=== "macOS"

    There's nothing installed to remove on macOS yet. To remove everything Open Backup Manager kept, delete the folders above.

=== "Linux"

    There's nothing installed to remove on Linux yet. To remove everything Open Backup Manager kept, delete the folders above.
