# Updates

=== "Windows"

    The app keeps itself up to date from the project's [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases). It checks when it starts and every 6 hours, and downloads any update in the background.

    When an update's ready, **Restart now** installs it straight away. Otherwise it installs the next time you quit the app, or when it next starts. To check straight away, use **Check for updates** in About.

    To stop it checking by itself, set [`automatic`](settings.md#updates.automatic) to `false`, and it only checks when you use **Check for updates**.

=== "macOS"

    Nothing updates itself on macOS yet. To update `obm` and `obmd`, download the latest release and unpack it over them, as [Getting started](getting-started.md#install-it) describes, then start `obmd` again. If you used `obm service install`, run it again to restart the engine on the new version.

=== "Linux"

    Nothing updates itself on Linux yet. To update `obm` and `obmd`, download the latest release and unpack it over them, as [Getting started](getting-started.md#install-it) describes, then start `obmd` again. If you used `obm service install`, run it again to restart the engine on the new version.

## Test versions

Test versions, also called prereleases, come out before each release so new things can be tried early.

=== "Windows"

    If you have a test version installed, you get test versions, and if you have a release, you only get releases. To choose for yourself, set [`prereleases`](settings.md#updates.prereleases) in the settings file.

=== "macOS"

    Test versions are on the [releases page](https://github.com/AmyJeanes/OpenBackupManager/releases) too, marked Pre-release. Nothing picks them for you, so download whichever you want.

=== "Linux"

    Test versions are on the [releases page](https://github.com/AmyJeanes/OpenBackupManager/releases) too, marked Pre-release. Nothing picks them for you, so download whichever you want.
