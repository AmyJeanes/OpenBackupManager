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

## Options

- `--home <folder>` keeps everything in that folder instead of the usual ones, as the `OBM_HOME` environment variable does. See [Files and folders](files.md). On macOS and Linux, the folder's path can't be too long, because the engine's socket goes in it and the OS limits how long a socket's path can be.
