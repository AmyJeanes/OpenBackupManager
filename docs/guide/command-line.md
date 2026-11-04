# Command line

`obm` is Open Backup Manager's command line, and `obmd` is its engine, which does the work in the background. Both run on Windows, macOS and Linux.

=== "Windows"

    The app installs both and puts `obm` on your PATH, so after installing, open a new terminal to use it.

=== "macOS"

    Download both and put them on your PATH, as [Getting started](getting-started.md#install-it) describes.

=== "Linux"

    Download both and put them on your PATH, as [Getting started](getting-started.md#install-it) describes.

## Commands

| Command | What it does |
| --- | --- |
| `obm --version` | Shows the version. |
| `obm status` | Shows whether the engine is running, and if so its version, process ID, how long it's been up, what started it, its data folder, where secrets are kept, and any errors or warnings in `config.toml`. An error in an edit leaves the engine using the last good settings until it's fixed, so it's still running. Exits with 1 only if it isn't running. |
| `obm service install` | macOS and Linux: starts the engine now and whenever you sign in, and again if it fails. See [The engine](#the-engine). |
| `obm service uninstall` | macOS and Linux: stops the engine, and stops it starting when you sign in. |
| `obm secrets migrate <store>` | Moves your secrets to another store, such as `file`, and keeps them there from now on, as [`secret-store`](settings.md#secret-store) says. It copies them all and checks each before removing them from the old store, so if anything goes wrong they're still where they were. The engine has to be running. |
| `obm config reload` | Has the engine read `config.toml` again and says what it found, for where it can't see the file change, such as on a network drive. Exits with 1 if the file has an error, which leaves the engine using the last good settings, or if the engine isn't running. On macOS and Linux, sending `obmd` SIGHUP does the same. |
| `obm config validate` | Checks `config.toml` for errors and warnings without the engine, such as before starting it. `--file <file>` checks another file instead, and `--env` also applies the `OBM_` environment variables set where you run it. Exits with 1 if there's an error. |
| `obm --help` | Lists the commands, and `obm <command> --help` describes one. |

`obm status`, `obm config reload` and `obm config validate` take `--json` too, to print what they found for scripts, as [For scripts](#for-scripts) describes.

## The engine

`obmd` runs the engine in the foreground with no UI, until you press ++ctrl+c++. Only one runs at a time for each data folder, and `obm` talks to it. It takes `--home` like `obm`, and `obmd --version` shows its version.

=== "Windows"

    The app starts the engine itself when it isn't running, and stops it when you quit the app. So `obmd` isn't on your PATH: it's in `%LocalAppData%\OpenBackupManager.App\current`. If you started the engine yourself, the app uses that one and leaves it running when you quit.

    To run the engine without the app, such as at sign-in, add `obmd` to Task Scheduler, with a trigger of At log on. It runs in a console window, and closing the window stops it. `obm service install` is only for macOS and Linux, since on Windows the app does its job.

=== "macOS"

    There's no app yet to start the engine for you. `obm service install` starts it now and whenever you sign in, through launchd, and starts it again if it fails. It runs until you sign out, and `obm service uninstall` stops it and takes it off. Or start `obmd` yourself, such as in a terminal you leave open. Closing the terminal doesn't stop it, so press ++ctrl+c++ first.

    macOS tells you it's added a background item, and lists it as `obmd` under Allow in the Background, in System Settings > General > Login Items & Extensions. Switching it off there stops the engine and keeps it from starting, and switching it on starts it again.

    If you've moved `obm` and `obmd` since, run `obm service install` again. With `--home`, each folder gets its own.

=== "Linux"

    There's no app yet to start the engine for you. `obm service install` starts it now and whenever you sign in, as a systemd user service, and starts it again if it fails. `obm service uninstall` stops it and takes it off. Or start `obmd` yourself, such as in a terminal you leave open. Closing the terminal doesn't stop it, so press ++ctrl+c++ first.

    The service runs while you're signed in. To keep it running when you're not, as on a server, turn on lingering with `loginctl enable-linger`, and it starts when the computer does. Secret Service is locked while you're not signed in with your password, so the engine can't read secrets kept there then. To keep them in a file only you can read instead, run `obm secrets migrate file`. `systemctl --user status openbackupmanager` shows how it's doing, and `journalctl --user -u openbackupmanager` what it printed. `systemctl --user reload openbackupmanager` has it read `config.toml` again, as `obm config reload` does. For a service installed by an older version, run `obm service install` again first.

    If you've moved `obm` and `obmd` since, run `obm service install` again. With `--home`, each folder gets its own.

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

## For scripts

With `--json`, `obm status`, `obm config reload` and `obm config validate` print what they found as JSON instead, as the engine gives it to `obm` and the app. New fields may be added, so a script should ignore ones it doesn't know. The exit codes are the same as without it. When there's nothing to print, such as when the engine isn't running, the error goes to stderr as usual and nothing goes to stdout, so check the exit code before reading it.

`obm status --json` prints something like this:

```json
{
  "version": "0.1.0",
  "processId": 4321,
  "started": "2026-11-04T09:12:44+00:00",
  "config": "/home/me/.config/openbackupmanager",
  "state": "/home/me/.local/state/openbackupmanager",
  "startedBy": "service",
  "secrets": {
    "store": "secret-service"
  },
  "configProblems": []
}
```

| Field | What it is |
| --- | --- |
| `version` | The engine's version. |
| `processId` | The engine's process ID. |
| `started` | When the engine started, with its offset from UTC. |
| `config` | The folder `config.toml` is in. |
| `state` | The engine's own folder. It's the same as `config` on Windows, or with `--home`. |
| `startedBy` | `app` for the app, which stops it when you quit, `service` for its service, or `other`. |
| `secrets.store` | Where secrets are kept, as [`secret-store`](settings.md#secret-store) names it. Left out until one is chosen. |
| `secrets.file` | The file they're in, when `store` is `file`. |
| `configProblems` | The errors and warnings in `config.toml`, from the engine's latest read of it. |

`obm config reload --json` and `obm config validate --json` print the errors and warnings they found as `problems`. They're in the order they were found, and each has these fields:

```json
{
  "problems": [
    {
      "line": 4,
      "kind": "shouldBeTrueOrFalse",
      "isError": true,
      "value": "updates.prereleases",
      "message": "updates.prereleases should be true or false"
    }
  ]
}
```

| Field | What it is |
| --- | --- |
| `line` | The line it's on, or 0 when it's in an environment variable. |
| `kind` | What's wrong, as one of the kinds below. More may be added. |
| `isError` | `true` for an error, which stops the file being used, or `false` for a warning, which doesn't. |
| `value` | The setting, section or remote ID it's about, or for `toml`, what's wrong with the TOML. Left out when it's about none of these. |
| `envVar` | The environment variable it's in. Left out when it's in the file. |
| `message` | What's wrong, in English, for showing to people. |

| Kind | What's wrong |
| --- | --- |
| `toml` | The file isn't valid TOML. `value` says why. |
| `shouldBeTrueOrFalse` | The setting should be `true` or `false`. |
| `shouldBeWholeNumber` | The setting should be a whole number. |
| `shouldBeText` | The setting should be text in quotes. |
| `notAChoice` | The setting isn't one of the values it takes. |
| `versionTooLow` | `version` should be 1 or more. |
| `newerVersion` | A newer version of the app wrote the file. This version reads it, but won't change it. |
| `shouldBeTable` | The section should be written as `[section]`. |
| `shouldBeTableArray` | The section should be written as `[[section]]`. |
| `remoteNeedsId` | A `[[remotes]]` has no `id`. |
| `remoteIdUsedTwice` | Two remotes have the same `id`. |
| `unknownSetting` | It isn't a setting this version knows, such as a misspelt one. |
| `unknownSection` | It isn't a section this version knows. |

## Options

`--home <folder>` keeps everything in that folder instead of the usual ones, as the `OBM_HOME` environment variable does. See [Files and folders](files.md).

=== "Windows"

    `obm` and `obmd` find each other by a name made from the folder, so its path can be as long as Windows allows.

=== "macOS"

    The folder's path can't be longer than about 87 characters, or fewer if it has accented or non-Latin letters, because the engine's socket goes in it and macOS limits how long a socket's path can be. If it's too long, `obmd` says so, and `obm` says the engine isn't running.

=== "Linux"

    The folder's path can't be longer than about 91 characters, or fewer if it has accented or non-Latin letters, because the engine's socket goes in it and Linux limits how long a socket's path can be. If it's too long, `obmd` says so, and `obm` says the engine isn't running.
