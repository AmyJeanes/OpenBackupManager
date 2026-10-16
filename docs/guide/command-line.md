# Command line

`obm` is Open Backup Manager's command line, and `obmd` is its engine, which does the app's work in the background. Both run on Windows, macOS and Linux. The Windows app doesn't install them yet.

## Commands

| Command | What it does |
| --- | --- |
| `obm --version` | Shows the version. |
| `obm status` | Shows whether the engine is running, and if so its version, process ID, how long it's been up and its data folder. Exits with 1 if it isn't running. |
| `obm --help` | Lists the commands, and `obm <command> --help` describes one. |

## The engine

`obmd` runs the engine in the foreground with no UI, until you press Ctrl+C. Only one runs at a time for each data folder, and `obm` talks to it. It takes `--home` like `obm`, and `obmd --version` shows its version.

## Running as administrator or root

You don't need administrator or root rights for Open Backup Manager. It backs up your own folders, so run `obm` and `obmd` as yourself, from a normal terminal.

**On Windows,** if you start `obmd` in a terminal opened with "Run as administrator", it stops and asks you to use a normal terminal instead. Running as administrator, the engine would be out of reach of the tray app, and of `obm` in a normal terminal. `obm` itself works fine as administrator.

When Windows only gives you administrator rights, such as over SSH, with the built-in Administrator account, or with UAC turned off, `obmd` runs as normal.

**On macOS and Linux,** `obm` and `obmd` won't run with `sudo`, or with `doas`, `pkexec` or `run0`. With sudo, the engine would run as root instead of you, and `obm` would look for root's engine instead of yours. Run them again without sudo.

If you're signed in as root itself, such as in a container, that's fine: root gets its own engine, like any other user.

**To back up a whole computer or a server,** use a tool made for that, such as [restic](https://restic.net), [Kopia](https://kopia.io) or [rclone](https://rclone.org). Open Backup Manager is made for your own folders.

## Options

- `--home <folder>` keeps everything in that folder instead of the usual ones, as the `OBM_HOME` environment variable does. See [Files and folders](files.md). On macOS and Linux, the folder's path can't be too long, because the engine's socket goes in it and the OS limits how long a socket's path can be.
