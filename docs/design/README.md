# Design docs

What the code can't tell you about each part of Open Backup Manager: the decisions and why, the rejected alternatives, accepted limits that shouldn't be "fixed", and traps. There's one doc per area, added as each part is built.

Each doc opens with a short paragraph on what the area is, and a code map: a line for each project or folder, naming the entry points worth searching for. Then comes a section per topic, with the reason beside each choice, then Accepted limits and Checking by hand.

- Keep a sentence only if someone reading the code would get something wrong without it, or retry something that was rejected.
- Leave out what the code and tests already say. Describe a test only to say why it's set up the way it is.
- A fact about one spot in the code goes in a short comment there instead.
- Evidence is a short clause or link, such as an upstream doc, another project or a measured number. Link it when it depends on someone else's documented behaviour, so it can be rechecked. The full story goes in the commit message.
- Each fact has one home. What people see or do is in the [user guide](../guide/README.md). A decision is in the doc for the area whose code makes it happen, and other docs link to it.

- [Bundled rclone](rclone-bundling.md): how the build fetches rclone and checks it's genuine
- [Configuration and files](config.md): where the app keeps its files on each OS, and config.toml
- [Engine and command line](engine.md): `obm`, and `obmd`, the engine that runs in the background
- [Platform layer](platform.md): what each OS does its own way, such as notifications and secrets
- [View models](view-models.md): what each screen shows and does, shared by every app
- [Windows app](windows-app.md): the WinUI tray app, its packages, and why it only builds on Windows
