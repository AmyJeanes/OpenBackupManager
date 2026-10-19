# Command line

`obm` is Open Backup Manager's command line, and `obmd` is its engine, which does the work in the background. Both run on Windows, macOS and Linux.

=== "Windows"

    The app installs both and puts `obm` on your PATH, so after installing, open a new terminal to use it.

=== "macOS"

    There's nothing to download for macOS yet, so for now you build them from source, as [CONTRIBUTING.md](https://github.com/AmyJeanes/OpenBackupManager/blob/main/CONTRIBUTING.md#build-and-test) describes.

=== "Linux"

    There's nothing to download for Linux yet, so for now you build them from source, as [CONTRIBUTING.md](https://github.com/AmyJeanes/OpenBackupManager/blob/main/CONTRIBUTING.md#build-and-test) describes.

## Commands

| Command | What it does |
| --- | --- |
| `obm --version` | Shows the version. |
| `obm status` | Shows whether the engine is running, and if so its version, process ID, how long it's been up and its data folder. Exits with 1 if it isn't running. |
| `obm --help` | Lists the commands, and `obm <command> --help` describes one. |

## The engine

`obmd` runs the engine in the foreground with no UI, until you press ++ctrl+c++. Only one runs at a time for each data folder, and `obm` talks to it. It takes `--home` like `obm`, and `obmd --version` shows its version.

=== "Windows"

    The app starts the engine itself when it isn't running, and stops it when you quit the app. So `obmd` isn't on your PATH: it's in `%LocalAppData%\OpenBackupManager.App\current`. If you started the engine yourself, such as at sign-in with Task Scheduler, the app uses that one and leaves it running when you quit.

=== "macOS"

    There's no app yet to start the engine for you, so start `obmd` yourself, such as in a terminal you leave open. Nothing starts it again if it stops.

=== "Linux"

    There's no app yet to start the engine for you, so start `obmd` yourself, such as in a terminal you leave open. Nothing starts it again if it stops.

## Running as administrator or root

You don't need administrator or root rights for Open Backup Manager. It backs up your own folders, so run `obm` and `obmd` as yourself, from a normal terminal.

=== "Windows"

    If you start `obmd` in a terminal opened with "Run as administrator", it stops and asks you to use a normal terminal instead. Running as administrator, the engine would be out of reach of the tray app, and of `obm` in a normal terminal. `obm` itself works fine as administrator. If you start the Open Backup Manager app with "Run as administrator", it starts itself again normally when possible or exits with a message if it can't.

    When Windows only gives you administrator rights, such as over SSH, with the built-in Administrator account, or with UAC turned off, `obmd` runs as normal.

=== "macOS"

    `obm` and `obmd` won't run with `sudo` or `doas`. With sudo, the engine would run as root instead of you, and `obm` would look for root's engine instead of yours. Run them again without sudo.

    If you're signed in as root itself, that's fine: root gets its own engine, like any other user.

=== "Linux"

    `obm` and `obmd` won't run with `sudo`, or with `doas`, `pkexec` or `run0`. With sudo, the engine would run as root instead of you, and `obm` would look for root's engine instead of yours. Run them again without sudo.

    If you're signed in as root itself, that's fine: root gets its own engine, like any other user.

**In a container,** such as with Docker, it's Linux inside, whatever your computer runs, so the Linux tab applies. Containers often run as root, which is fine.

**To back up a whole computer or a server,** use a tool made for that, such as [restic](https://restic.net), [Kopia](https://kopia.io) or [rclone](https://rclone.org). Open Backup Manager is made for your own folders.

## Options

`--home <folder>` keeps everything in that folder instead of the usual ones, as the `OBM_HOME` environment variable does. See [Files and folders](files.md).

=== "Windows"

    `obm` and `obmd` find each other by a name made from the folder, so its path can be as long as Windows allows.

=== "macOS"

    The folder's path can't be longer than about 87 characters, or fewer if it has accented or non-Latin letters, because the engine's socket goes in it and macOS limits how long a socket's path can be. If it's too long, `obmd` says so, and `obm` says the engine isn't running.

=== "Linux"

    The folder's path can't be longer than about 91 characters, or fewer if it has accented or non-Latin letters, because the engine's socket goes in it and Linux limits how long a socket's path can be. If it's too long, `obmd` says so, and `obm` says the engine isn't running.
