# Configuration and files

Where the app keeps its files on each OS.

## How it works

- Core's `AppPaths` says where each kind of file goes: `config.toml` in Config, the engine's state such as each folder's index and our own `rclone.conf` in State, and logs and cache in their own folders. Every app and the CLI use it.
- Each OS has its usual folders:

  | Kind | Windows | macOS | Linux |
  | --- | --- | --- | --- |
  | Config | `%LocalAppData%\OpenBackupManager\` | `~/.config/openbackupmanager/` | `$XDG_CONFIG_HOME/openbackupmanager/`, default `~/.config/...` |
  | State | `%LocalAppData%\OpenBackupManager\` | `~/Library/Application Support/OpenBackupManager/` | `$XDG_STATE_HOME/openbackupmanager/`, default `~/.local/state/...` |
  | Logs | `%LocalAppData%\OpenBackupManager\logs\` | `~/Library/Logs/OpenBackupManager/` | `$XDG_STATE_HOME/openbackupmanager/logs/` |
  | Cache | `%LocalAppData%\OpenBackupManager\cache\` | `~/Library/Caches/OpenBackupManager/` | `$XDG_CACHE_HOME/openbackupmanager/`, default `~/.cache/...` |

- A home folder, from `--home` or the `OBM_HOME` environment variable, puts everything in one folder instead, for tests, portable setups and copies kept apart. Windows' own layout is already one folder, so the Windows app always works from a home: see [Windows app](windows-app.md).

## Why

- **The paths are worked out in Core, from the OS and its environment variables,** rather than with .NET's `Environment.SpecialFolder`, which maps differently on each OS and ignores the XDG variables. Being in Core, every OS's layout is tested on every CI runner.
- **Windows uses Local, not Roaming, AppData,** because the settings hold this device's folder paths, which mustn't follow the user to another device.
- **macOS keeps config in `~/.config`,** where command line tools like rclone and `gh` keep theirs, so it works with dotfiles. `XDG_CONFIG_HOME` moves it, as it does for them. Everything else follows Apple's layout, which also shows the logs in Console.
- **A relative path in an XDG variable is ignored,** as the XDG spec says.
