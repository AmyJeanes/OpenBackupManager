# Settings

Open Backup Manager keeps this device's settings in a text file, `config.toml`. The app writes it, and you can edit it manually too.

## Where the file is

=== "Windows"

    `%LocalAppData%\OpenBackupManager\config.toml`

    If the app was started with `--home <folder>`, or the `OBM_HOME` environment variable is set, the file is in that folder instead.

=== "macOS"

    There's no settings file on macOS yet. The settings below are all for the app, which comes later.

=== "Linux"

    There's no settings file on Linux yet. The settings below are all for the app, which comes later.

## Editing the file

The file is [TOML](https://toml.io): settings are `name = value`, grouped into sections such as `[updates]`, and anything after a `#` is a comment. Anything left out has its default.

- **Changes apply as soon as you save,** unless the file is on a network drive, where the app can't tell when it changes. There, run `obm config reload` after editing it, or restart the app.
- **A mistake while the app is running doesn't break anything.** It keeps using the last good settings, and shows a notification saying what's wrong and on which line, with a button that shows the file. Once you fix it and save, the fixed settings apply.
- **A mistake when the app starts stops it starting,** as there are no good settings to keep using. A message says what's wrong and where the file is, and once it's fixed, start the app again. The engine won't start either.
- **A setting the app doesn't know is only a warning,** written to [the app's log](files.md#logs), so a typo in a setting's name is worth checking for there if a change seems to do nothing.
- **To go back to the defaults,** delete the file. The app writes a fresh one the next time it starts.
- **When the app changes a setting itself,** it keeps your comments and layout, and keeps the previous file as `config.toml.bak`.

## Environment variables

Each setting can also be set with an environment variable, shown with it below, such as `OBM_UPDATES_AUTOMATIC` for `automatic` in `[updates]`. An env var wins over the file configuration.

## Settings

Each setting goes at the top of the file, or under its section's line, such as `[updates]`, as its example shows. Settings work on every OS unless they're marked otherwise.

<!-- generated:settings -->

| Setting | Default | Works on |
| --- | --- | --- |
| [`start-at-login`](#start-at-login) | `true` when installed | Windows |
| [`secret-store`](#secret-store) | The OS's own store | All |
| [`updates.automatic`](#updates.automatic) | `true` | Windows |
| [`updates.prereleases`](#updates.prereleases) | Follows what's installed | All |

### `start-at-login` {#start-at-login}

<span class="obm-badge" title="Works on" markdown>:fontawesome-brands-windows: Windows only</span>

Starts the app in the tray when you sign in.

<div class="obm-facts" markdown>

Values
:   `true` or `false`

Default
:   `true` for the installed app, unless it was started with another home

Environment variable
:   `OBM_START_AT_LOGIN`

</div>

```toml
start-at-login = false
```

You can also turn it off in Windows' Settings > Apps > Startup, or Task Manager's Startup apps, which the app leaves alone even while this is `true`.

### `secret-store` {#secret-store}

Where this device keeps its secrets, such as the password that protects your sign-ins.

<div class="obm-facts" markdown>

Values
:   `credential-manager` on Windows, `keychain` on macOS, `secret-service` on Linux, or `file` on any

Default
:   `credential-manager` on Windows and `keychain` on macOS. Linux has no default, so one has to be chosen

Environment variable
:   `OBM_SECRET_STORE`

</div>

```toml
secret-store = "file"
```

`file` keeps them in `secrets.json`, in the engine's own folder (see [Files and folders](files.md)), protected by nothing but its permissions, for when the OS's store can't be used, such as on a server or over SSH. On Windows and macOS, any program running as you can read what's in the OS's store too. On Linux, `secret-service` is locked whenever you're not signed in with your password, so it can't be read with lingering on and nobody signed in, or after an automatic sign-in until it's unlocked. To change it once secrets are saved, run `obm secrets migrate` with the store to move them to, which changes this too (see [Command line](command-line.md#commands)). Changing it by hand leaves them behind in the old store.

### `[updates]` {#updates}

How Open Backup Manager keeps itself up to date.

#### `automatic` {#updates.automatic}

<span class="obm-badge" title="Works on" markdown>:fontawesome-brands-windows: Windows only</span>

Checks for updates when the app starts and every few hours, and downloads them ready to install.

<div class="obm-facts" markdown>

Values
:   `true`, or `false` to only check when you ask

Default
:   `true`

Environment variable
:   `OBM_UPDATES_AUTOMATIC`

</div>

```toml
[updates]
automatic = false
```

While it's off, **Check for updates** in About still checks straight away. Turning it back on checks straight away too.

#### `prereleases` {#updates.prereleases}

Gets test versions before they're released, so new things can be tried early.

<div class="obm-facts" markdown>

Values
:   `true` for test versions, or `false` for released versions only

Default
:   Follows what's installed, so a test version gets test versions

Environment variable
:   `OBM_UPDATES_PRERELEASES`

</div>

```toml
[updates]
prereleases = true
```

A change takes effect from the next [check for updates](updates.md): on Windows, the app's next check, which it makes every few hours, or **Check for updates** in About, and on macOS and Linux, the next time you run `obm update`.

<!-- /generated:settings -->
