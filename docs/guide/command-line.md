# Command line

`obm` is Open Backup Manager's command line. It runs on Windows, macOS and Linux. The Windows app doesn't install it yet.

## Commands

| Command | What it does |
| --- | --- |
| `obm --version` | Shows the version. |
| `obm daemon` | Runs the engine, which does the app's work in the background, in the foreground with no UI, until you press Ctrl+C. Only one runs at a time for each data folder. |
| `obm --help` | Lists the commands, and `obm <command> --help` describes one. |

## Options

- `--home <folder>` keeps everything in that folder instead of the usual ones, as the `OBM_HOME` environment variable does. See [Files and folders](files.md).
