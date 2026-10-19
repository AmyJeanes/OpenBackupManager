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

- **Changes apply as soon as you save,** unless the file is on a network drive, where the app can't tell when it changes. There, restart the app after editing it.
- **A mistake doesn't break anything.** If the file has an error, the app keeps using the last good settings, and shows a notification saying what's wrong and on which line, with a button that shows the file. Once you fix it and save, the fixed settings apply.
- **A setting the app doesn't know is only a warning,** written to [the app's log](files.md#logs), so a typo in a setting's name is worth checking for there if a change seems to do nothing.
- **To go back to the defaults,** delete the file. The app writes a fresh one the next time it starts.
- **When the app changes a setting itself,** it keeps your comments and layout, and keeps the previous file as `config.toml.bak`.

## Settings

Each setting goes at the top of the file, or under its section's line, such as `[updates]`, as its example shows. Settings work on every OS unless they're marked otherwise.

| Setting | Default | Works on |
| --- | --- | --- |
| [`start-at-login`](#start-at-login) | `true` when installed | Windows |
| [`updates.prereleases`](#updates.prereleases) | Follows what's installed | Windows |

### `start-at-login` {#start-at-login}

<span class="obm-badge" title="Works on" markdown>:fontawesome-brands-windows: Windows only</span>

Starts the app in the tray when you sign in.

<div class="obm-facts" markdown>

Values
:   `true` or `false`

Default
:   `true` for the installed app, unless it was started with another home

</div>

```toml
start-at-login = false
```

You can also turn it off in Windows' Settings > Apps > Startup, or Task Manager's Startup apps, which the app leaves alone even while this is `true`.

### `[updates]` {#updates}

How the app keeps itself up to date.

#### `prereleases` {#updates.prereleases}

<span class="obm-badge" title="Works on" markdown>:fontawesome-brands-windows: Windows only</span>

Gets test versions before they're released, so new things can be tried early.

<div class="obm-facts" markdown>

Values
:   `true` for test versions, or `false` for released versions only

Default
:   Follows what's installed, so a test version gets test versions

</div>

```toml
[updates]
prereleases = true
```

A change takes effect from the next [check for updates](updates.md), which the app makes every few hours, or straight away from **Check for updates** in About.
