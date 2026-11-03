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
- `config.toml`, in Config, holds this device's settings. It starts with `version = 1` and settings for the whole app, such as `start-at-login`, then sections such as `[updates]`, and a `[[remotes]]` entry for each remote, keyed by its `id`. Secrets never go in it. Anything left out has its default, and a missing file means all the defaults.
- Core's `ConfigFile` reads it. A syntax error, a value of the wrong type or a remote without a unique `id` is an error naming the line, and a file with an error isn't used. An unknown key or section is only a warning.
- Every setting except those in a list of sections has an environment variable, `OBM_` and its path in capitals with `.` and `-` as `_`, such as `OBM_UPDATES_AUTOMATIC`. `ConfigFile.Read`, and so the watcher, checks each one that's set as it would the same value in the file, then writes it into the file's text before Tomlyn reads it, so it wins over the file. A mistake in one is an error naming the env var, and an empty one counts as not set. Writing never applies them, so their values never end up in the file.
- A file whose `version` is newer than this version knows, such as after rolling back an update, is read for the settings this version understands but never written, so nothing newer is lost.
- Writing re-reads the file first and won't go ahead while it has an error. It changes the one value in the file's syntax tree, so comments and layout written by hand stay, checks the result reads cleanly, copies the old file to `config.toml.bak`, then writes a temporary file and moves it over the old one. Windows won't move it while something is reading the old file, so it tries again for up to a second.
- Each problem has a kind, which Core's `Strings.resx` turns into a message in the user's language, such as `Line 3: updates.prereleases should be true or false`. Tomlyn's own messages, such as for a syntax error, are only in English, so they're passed on as they are. Logs always get the English.
- `ConfigWatcher` keeps the settings up to date with the file, so an edit applies as soon as it's saved, whether by hand or by the app. An edit with an error leaves the last good settings in use until it's fixed. An error in its first read, from the file or an env var, leaves no good settings at all, so it throws a `ConfigException` instead, and nothing starts. The engine reads them as it's built, so `obmd` exits saying what's wrong, and the Windows app checks them before anything else and stops, saying why in a message box, as it does for its other reasons not to start. The watcher logs every problem, `obm status` lists them all, errors first, and the Windows app also shows the first error of an edit in a notification, which can show the file. Deleting the file goes back to the defaults.
- Reading the file never stops anyone else saving it, and the watcher waits until the file has been left alone for a moment, so it doesn't read a save halfway through.
- If `config.toml` or its folder is a link, as dotfiles managers make them, the watcher also watches the folder the link points to, since that's where edits happen.

## Why

- **TOML, read and written with Tomlyn.** TOML has comments and explicit types, and no indentation rules or YAML's `no` meaning false. Tomlyn's syntax tree keeps every character of the file, so the app can change one value without losing what someone wrote by hand. YAML and JSON libraries rewrite the whole file and drop comments.
- **Unknown keys are only warnings,** so an older version still runs with a file that has a newer version's settings. A typo still shows up, with its line. `version` only goes up for a change an older version would misread.
- **Types are checked against the list of known keys** before Tomlyn maps the file, which gives messages like `line 3: updates.prereleases should be true or false` rather than the serializer's own.
- **Nothing starts on settings with an error.** Going on with the defaults could undo what the file says without anyone noticing, such as putting secrets in another store or turning start at login back on. Once there are good settings, keeping them through a bad edit is safe, as they're the ones meant.
- **Env vars are applied while the file is read,** so code reads one `AppConfig` and never has to know where a value came from, and an env var is checked by the same code as the file. Written into the text, they go through Tomlyn the same way, with no code for each setting.
- **The new file goes in with one rename.** `File.Replace` moves the old file aside before moving the new one in, so for a moment there's no file and the watcher would go back to the defaults.
- **An empty file is ignored until it has something in it.** Saving often empties the file before writing it, and reading it then would also go back to the defaults for a moment.
- **The paths are worked out in Core, from the OS and its environment variables,** rather than with .NET's `Environment.SpecialFolder`, which maps differently on each OS and ignores the XDG variables. Being in Core, every OS's layout is tested on every CI runner.
- **Windows uses Local, not Roaming, AppData,** because the settings hold this device's folder paths, which mustn't follow the user to another device.
- **macOS keeps config in `~/.config`,** where command line tools like rclone and `gh` keep theirs, so it works with dotfiles. `XDG_CONFIG_HOME` moves it, as it does for them. Everything else follows Apple's layout, which also shows the logs in Console.
- **A relative path in an XDG variable is ignored,** as the XDG spec says.
- **Our own reading and writing, not Microsoft.Extensions.Configuration (checked Nov 2026).** Its file provider clears every setting when a reload can't parse the file or finds it missing, where ours keeps the last good settings. Its watcher doesn't follow links, its settings lose their line numbers, an unknown key can only throw, its environment variables split keys on `__`, and it can't write back. Its TOML providers are years out of date (Tomlyn.Extensions.Configuration) or don't keep the file's layout (CsToml). Tomlyn's syntax tree is the only .NET TOML library that writes back every character as it was. The folder libraries (Xdg.Directories, Directories.Net, FVNever.AppDirs) put macOS config in `~/Library`, use XDG variables even when they're relative, or have no state folder.

## Accepted limits

- **On network drives and some mounts, an edit needs `obm config reload`.** The watcher relies on the OS's change notifications, which network shares, FUSE and some container mounts don't send, and which WSL doesn't send for Windows files under `/mnt`. There, `obm config reload`, SIGHUP on macOS and Linux, or a restart applies it. The config folder is a local per-user folder unless `--home` points somewhere else, so checking the file on a timer isn't worth it.
- **A missed change isn't retried.** The next save, or starting the app again, picks it up.
