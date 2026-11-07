# Updates

=== "Windows"

    The app keeps itself up to date from the project's [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases). It checks when it starts and every 6 hours, and downloads any update in the background.

    When an update's ready, **Restart now** installs it straight away. Otherwise it installs the next time you quit the app, or when it next starts. To check straight away, use **Check for updates** in About.

    To stop it checking by itself, set [`automatic`](settings.md#updates.automatic) to `false`, and it only checks when you use **Check for updates**.

=== "macOS"

    Run `obm update` to update `obm` and `obmd` to the newest version from the [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases). It checks the download against the checksum GitHub gives for it, then replaces them where they are. If you used `obm service install`, the service restarts the engine on the new version, and `obm update` waits to say it has. If you started `obmd` yourself, restart it. `obm update --check` only says whether there's a newer version, and nothing checks by itself. To be sure the new ones came from the project's build, check them with `gh attestation verify`, as [Checking a download](checking-downloads.md) describes.

=== "Linux"

    Run `obm update` to update `obm` and `obmd` to the newest version from the [releases on GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases). It checks the download against the checksum GitHub gives for it, then replaces them where they are. If you used `obm service install`, the service restarts the engine on the new version, and `obm update` waits to say it has. If you started `obmd` yourself, restart it. `obm update --check` only says whether there's a newer version, and nothing checks by itself. To be sure the new ones came from the project's build, check them with `gh attestation verify`, as [Checking a download](checking-downloads.md) describes.

    If you installed the `.deb` or `.rpm`, install the newer package instead, as `obm update` tells you. In Docker, update the image, as [Docker](docker.md) describes.

## Test versions

Test versions, also called prereleases, come out before each release so new things can be tried early.

If you have a test version, updates get you test versions, and if you have a release, only releases. To choose for yourself, set [`prereleases`](settings.md#updates.prereleases) in the settings file.
