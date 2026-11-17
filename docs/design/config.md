# Configuration and files

Where the app keeps its files on each OS, and its settings file, `config.toml`, which every program reads through Core, and which the engine and the app keep up to date as it's edited. Where each file is, and how to edit the settings, is in the guide's [Files and folders](../guide/files.md) and [Settings](../guide/settings.md).

## Code map

- Core's `AppPaths` has each OS's folders, and `AppHome` picks the home folder from `--home` or `OBM_HOME`, which puts everything in one folder. On Windows, where everything is in one folder anyway, every program works from a home. `PrivateFiles` makes folders and files only the user can open on macOS and Linux.
- Core's `Configuration` folder: `Settings` lists every setting, `ConfigFile` reads and writes `config.toml`, `ConfigWatcher` keeps the settings up to date with it, and `ConfigResult` holds the problems it found. `AppConfig` is the settings themselves.

## Folders

- **The paths are worked out in Core, from the OS and its environment variables,** rather than with .NET's [`Environment.SpecialFolder`](https://github.com/dotnet/runtime/blob/v11.0.0/src/libraries/System.Private.CoreLib/src/System/Environment.GetFolderPathCore.Unix.cs#L118-L131), which maps differently on each OS, has no state folder, and doesn't read `XDG_STATE_HOME` or `XDG_CACHE_HOME`. Being in Core, every OS's layout is tested on every CI runner.
- **Windows uses Local, not Roaming, AppData,** because the settings hold this device's folder paths, which mustn't follow the user to another device.
- **macOS keeps config in `~/.config`,** where command line tools like [rclone](https://github.com/rclone/rclone/blob/v1.76.0/docs/content/docs.md#--config-string) and [`gh`](https://github.com/cli/go-gh/blob/v2.17.0/pkg/config/config.go#L274-L287) keep theirs, so it works with dotfiles. `XDG_CONFIG_HOME` moves it, as it does for them. Everything else follows Apple's layout, which also [shows the logs in Console](https://developer.apple.com/library/archive/documentation/FileManagement/Conceptual/FileSystemProgrammingGuide/MacOSXDirectories/MacOSXDirectories.html#//apple_ref/doc/uid/TP40010672-CH10-101001-BCIGBDJF).

## config.toml

- **TOML, read and written with Tomlyn.** TOML has [comments](https://github.com/toml-lang/toml/blob/1.1.0/toml.md#comment) and [explicit types](https://github.com/toml-lang/toml/blob/1.1.0/toml.md#keyvalue-pair), and [no indentation rules](https://github.com/toml-lang/toml/blob/1.1.0/toml.md#keys) or [YAML 1.1's `no` meaning false](https://yaml.org/type/bool.html). [Tomlyn's syntax tree keeps every character of the file](https://github.com/xoofx/Tomlyn/blob/2.11.1/site/docs/low-level.md#syntaxparser-full-fidelity-syntax-tree), so the app can change one value without losing what someone wrote by hand. YAML and JSON libraries rewrite the whole file and drop comments, as Tomlet, Tommy and [CsToml](https://github.com/prozolic/CsToml/blob/v1.8.6/src/CsToml/TomlDocument.cs#L75-L84) do for TOML.
- **Unknown keys are only warnings,** so an older version still runs with a file that has a newer version's settings. A typo still shows up, with its line.
- **Nothing starts on settings with an error.** Going on with the defaults could undo what the file says without anyone noticing, such as putting secrets in another store or turning start at login back on. Once there are good settings, keeping them through a bad edit is safe, as they're the ones meant.
- **Env vars are applied while the file is read,** so code reads one `AppConfig` and never has to know where a value came from, and an env var is checked by the same code as the file. Written into the text, they go through Tomlyn the same way, with no code for each setting.

## Built ourselves

- **Our own reading and writing, not Microsoft.Extensions.Configuration (checked Nov 2026).** Its [file provider clears every setting](https://github.com/dotnet/runtime/blob/v11.0.0/src/libraries/Microsoft.Extensions.Configuration.FileExtensions/src/FileConfigurationProvider.cs#L125-L140) when a reload can't parse the file or finds it missing, where ours keeps the last good settings. Its watcher doesn't follow links unless it polls every 4 seconds, its settings lose their line numbers, its binder either ignores an unknown key or throws, and it can't write back. The folder libraries put macOS config in `~/Library` and use XDG variables even when they're relative ([Xdg.Directories](https://github.com/xdg-net/Xdg.Directories/blob/v0.2.0/src/Xdg.Directories/BaseDirectory.cs#L22-L31), Directories.Net), have no state folder (Directories.Net), or have only a state folder (FVNever.AppDirs).

## Accepted limits

- **On network drives and some mounts, an edit needs `obm config reload`.** The watcher relies on the OS's change notifications, which [Linux doesn't send for network filesystems](https://git.kernel.org/pub/scm/docs/man-pages/man-pages.git/tree/man/man7/inotify.7?h=man-pages-6.19), [FUSE](https://github.com/libfuse/libfuse/wiki/Fsnotify-and-FUSE) and some container mounts don't send for changes made elsewhere, and [WSL 2 doesn't send](https://github.com/microsoft/WSL/issues/4739) when Windows programs change files under `/mnt`. There, `obm config reload`, SIGHUP on macOS and Linux, or a restart applies it, as it does when the watcher can't start, such as when other programs have used up the user's inotify instances, which the engine logs and carries on. The config folder is a local per-user folder unless `--home` points somewhere else, so checking the file on a timer isn't worth it.
- **On Windows, `obm config reload` only reaches the engine,** not the app, so the app's own settings, such as `[updates]`, wait for it to restart.
- **A missed change isn't retried.** The next save, or starting the app again, picks it up.
