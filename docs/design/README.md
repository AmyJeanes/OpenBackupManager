# Design docs

How each part of Open Backup Manager works and why: the decisions, the alternatives that were rejected, the evidence, and accepted limits that shouldn't be "fixed". There's one doc per area, added as each part is built.

- [Bundled rclone](rclone-bundling.md): how the build fetches rclone and checks it's genuine
- [Configuration and files](config.md): where the app keeps its files on each OS, and config.toml
- [Engine and command line](engine.md): `obm`, and the engine it runs in the background
- [Platform layer](platform.md): what each OS does its own way, such as notifications and secrets
- [View models](view-models.md): what each screen shows and does, shared by every app
- [Windows app](windows-app.md): the WinUI tray app, its packages, and why it only builds on Windows
