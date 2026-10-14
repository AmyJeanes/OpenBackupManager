# Settings file

Open Backup Manager keeps this device's settings in a text file, `config.toml`. The app writes it, and you can edit it manually too.

## Where it is

| OS | File |
| --- | --- |
| Windows | `%LocalAppData%\OpenBackupManager\config.toml` |
| macOS | `~/.config/openbackupmanager/config.toml` |
| Linux | `~/.config/openbackupmanager/config.toml`, or under `$XDG_CONFIG_HOME` if it's set |

If the app was started with `--home <folder>`, or the `OBM_HOME` environment variable is set, the file is in that folder instead.

## Editing it

The file is [TOML](https://toml.io): settings are `name = value`, grouped into sections such as `[updates]`, and anything after a `#` is a comment. Anything left out has its default.

- **Changes apply as soon as you save,** unless the file is on a network drive, where the app can't tell when it changes. There, restart the app after editing it.
- **A mistake doesn't break anything.** If the file has an error, the app keeps using the last good settings and tells you what's wrong and on which line. On Windows this shows as a notification, with a button that shows the file. Once you fix it and save, the fixed settings apply.
- **A setting the app doesn't know is only a warning,** written to [the log](files.md#logs), so a typo in a setting's name is worth checking for there if a change seems to do nothing.
- **To go back to the defaults,** delete the file. The app writes a fresh one the next time it starts.
- **When the app changes a setting itself,** it keeps your comments and layout, and keeps the previous file as `config.toml.bak`.

## Settings

### `[updates]`

| Setting | Values | Default |
| --- | --- | --- |
| `prereleases` | `true` to get test versions before they're released, `false` for released versions only | Follows what's installed: a test version gets test versions |

```toml
[updates]
prereleases = true
```

A change takes effect from the next check for updates, which the app makes every few hours, or straight away from **Check for updates** in About.
