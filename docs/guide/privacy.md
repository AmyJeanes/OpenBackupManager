# Privacy

Open Backup Manager doesn't collect anything about you or how you use it. There's no account, no analytics and no telemetry, and crash reports stay on your device until you choose to share one.

## What connects to the internet

=== "Windows"

    Only the app's update checks. When it starts and every 6 hours, it asks [GitHub](https://github.com/AmyJeanes/OpenBackupManager/releases) whether there's a newer version, and downloads it from there if so. GitHub sees your IP address and what's downloaded, as with any website, under [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).

    To stop it checking by itself, set [`automatic`](settings.md#updates.automatic) to `false`. It then only checks when you use **Check for updates** in About.

    Links in the app, such as to GitHub from About or a notification, open in your browser when you choose them.

=== "macOS"

    Nothing yet. `obm` and `obmd` only talk to each other, on your Mac.

=== "Linux"

    Nothing yet. `obm` and `obmd` only talk to each other, on your computer.

## Your backups

Backing up isn't built yet. Once it is, your files will only go to the storage you connect, such as OneDrive, and that service's own privacy policy applies to what you keep there.
