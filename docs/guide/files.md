# Files and folders

Where Open Backup Manager keeps its files, and what's in them.

## Your data

Everything the app keeps about this device goes in one folder on Windows, and in the usual folders for each kind of file on macOS and Linux:

| | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Settings | `%LocalAppData%\OpenBackupManager\` | `~/.config/openbackupmanager/` | `~/.config/openbackupmanager/` |
| Logs | `%LocalAppData%\OpenBackupManager\logs\` | `~/Library/Logs/OpenBackupManager/` | `~/.local/state/openbackupmanager/logs/` |

- **Settings:** `config.toml`, this device's settings, and `config.toml.bak`, the version before the app last changed it. See [Settings file](config.md).
- **Logs:** a file for each day, described below.

The Windows app also keeps `last-version` next to its settings: the version that last ran, so it can say when it's been updated.

On Linux, `XDG_CONFIG_HOME` and `XDG_STATE_HOME` move these folders as they do for other apps. If the app was started with `--home <folder>`, or the `OBM_HOME` environment variable is set, everything goes in that folder instead.

## Logs

The app writes what it does to a log: starting up, checking for and installing updates, and any problems with the settings file. If something goes wrong, the log is the first place to look, and it's worth attaching to a bug report.

- There's a file for each day, such as `app-20261014.log`. A day that writes more than 10 MB carries on in `app-20261014_001.log`, and so on.
- Only the newest 7 files are kept, which is usually about a week.
- Passwords and tokens are never written to the logs, so they're safe to share.

## The app itself

On Windows the app is installed in `%LocalAppData%\OpenBackupManager.App\`, along with any update it has downloaded. Updates replace what's there, so don't keep anything of your own in it.

## Uninstalling

Uninstalling removes the app, its Start menu shortcut and its notifications, but leaves your data, so installing it again picks up where you left off. To remove everything, delete the data folder too.
