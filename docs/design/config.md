# Configuration and files

Where the app keeps its files on each OS, and its settings file, `config.toml`.

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
- `config.toml`, in Config, holds this device's settings. It starts with `version = 1`, then sections such as `[updates]`, and a `[[remotes]]` entry for each remote, keyed by its `id`. Secrets never go in it. Anything left out has its default, and a missing file means all the defaults.
- Core's `ConfigFile` reads it. A syntax error, a value of the wrong type or a remote without a unique `id` is an error naming the line, and a file with an error isn't used. An unknown key or section is only a warning.
- A file whose `version` is newer than this version knows, such as after rolling back an update, is read for the settings this version understands but never written, so nothing newer is lost.
- Writing re-reads the file first and won't go ahead while it has an error. It changes the one value in the file's syntax tree, so comments and layout written by hand stay, checks the result reads cleanly, copies the old file to `config.toml.bak`, then writes a temporary file and moves it over the old one.
- `ConfigWatcher` keeps the settings up to date with the file, so an edit applies as soon as it's saved, whether by hand or by the app. An edit with an error leaves the last good settings in use, and says what's wrong, until it's fixed. Deleting the file goes back to the defaults.
- If `config.toml` or its folder is a link, as dotfiles managers make them, the watcher also watches the folder the link points to, since that's where edits happen.

## Why

- **TOML, read and written with Tomlyn.** TOML has comments and explicit types, and no indentation rules or YAML's `no` meaning false. Tomlyn's syntax tree keeps every character of the file, so the app can change one value without losing what someone wrote by hand. YAML and JSON libraries rewrite the whole file and drop comments.
- **Unknown keys are only warnings,** so an older version still runs with a file that has a newer version's settings. A typo still shows up, with its line. `version` only goes up for a change an older version would misread.
- **Types are checked against the list of known keys** before Tomlyn maps the file, which gives messages like `line 3: updates.prereleases should be true or false` rather than the serializer's own.
- **The new file goes in with one rename.** `File.Replace` moves the old file aside before moving the new one in, so for a moment there's no file and the watcher would go back to the defaults.
- **An empty file is ignored until it has something in it.** Saving often empties the file before writing it, and reading it then would also go back to the defaults for a moment.
- **The paths are worked out in Core, from the OS and its environment variables,** rather than with .NET's `Environment.SpecialFolder`, which maps differently on each OS and ignores the XDG variables. Being in Core, every OS's layout is tested on every CI runner.
- **Windows uses Local, not Roaming, AppData,** because the settings hold this device's folder paths, which mustn't follow the user to another device.
- **macOS keeps config in `~/.config`,** where command line tools like rclone and `gh` keep theirs, so it works with dotfiles. `XDG_CONFIG_HOME` moves it, as it does for them. Everything else follows Apple's layout, which also shows the logs in Console.
- **A relative path in an XDG variable is ignored,** as the XDG spec says.

## Accepted limits

- **On network drives and some mounts, an edit only applies after a restart.** The watcher relies on the OS's change notifications, which network shares, FUSE and some container mounts don't send, and which WSL doesn't send for Windows files under `/mnt`. The config folder is a local per-user folder unless `--home` points somewhere else, so checking the file on a timer isn't worth it.
- **A missed change isn't retried.** The next save, or starting the app again, picks it up.
