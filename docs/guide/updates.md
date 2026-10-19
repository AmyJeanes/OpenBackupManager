# Updates

=== "Windows"

    The app keeps itself up to date from the project's [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases). It checks when it starts and every 6 hours, and downloads any update in the background.

    When an update's ready, **Restart now** installs it straight away. Otherwise it installs the next time you quit the app, or when it next starts. To check straight away, use **Check for updates** in About.

    To stop it checking by itself, set [`automatic`](settings.md#updates.automatic) to `false`, and it only checks when you use **Check for updates**.

=== "macOS"

    Nothing updates itself on macOS yet, since there's nothing to download there. To update `obm` and `obmd`, build them again from the latest source.

=== "Linux"

    Nothing updates itself on Linux yet, since there's nothing to download there. To update `obm` and `obmd`, build them again from the latest source.

## Test versions

Test versions, also called prereleases, come out before each release so new things can be tried early.

=== "Windows"

    If you have a test version installed, you get test versions, and if you have a release, you only get releases. To choose for yourself, set [`prereleases`](settings.md#updates.prereleases) in the settings file.

=== "macOS"

    There are no test versions for macOS yet.

=== "Linux"

    There are no test versions for Linux yet.
