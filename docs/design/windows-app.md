# Windows app

The Windows app is a native WinUI 3 tray app. It's unpackaged, a plain folder with an exe and no MSIX, and carries its own copy of the Windows App SDK, so nothing else needs installing, and Velopack installs and updates it for the user alone. Like `obm`, it's a client of the engine for its home, which it starts and stops (see [Engine and command line](engine.md)). What people see and do is in the guide's [Getting started](../guide/getting-started.md), [Updates](../guide/updates.md), [Settings](../guide/settings.md) and [Files](../guide/files.md).

## Code map

- `OpenBackupManager.App.Windows`: the app. `Program` has its own `Main`, which runs Velopack's hooks, hands over to a copy that's already running and starts again without admin rights before it builds the services. `AppActions` is what the tray, notifications and windows ask the app to do. Then `Tray`, `StatusFlyout` and `FlyoutPlacement`, `EngineConnection`, `Updater`, `StartAtLoginSetting`, `CrashCapture`, `SessionEnd`, `FirstDraw` and `UiSynchronizationContext`.
- `OpenBackupManager.Platform.Windows`: notifications, Credential Manager, the Run key, PATH, `DesktopShell` and `CrashRestart` (see [Platform layer](platform.md)). What each window shows comes from the shared [view models](view-models.md).
- `OpenBackupManager.App.Windows.Tests` has the unit tests. `OpenBackupManager.App.Windows.UITests` drives the published app through UI Automation with FlaUI, so it's in the `Sandbox` category, and its `InstallTests` replace the installed app, so they're Explicit.
- `tools/Publish-App.ps1` publishes the app, `obmd` and `obm` into one folder, `tools/Publish-TestUpdate.ps1` packs test updates, and `tools/Get-Version.ps1` works out the version. `.github/scripts/Clear-Desktop.ps1` readies GitHub's runners for the UI tests.

## Name and packaging

- **People see Open Backup Manager, with spaces.** Start menu and store searches match each word, and screen readers read it naturally. The joined `OpenBackupManager` stays wherever it's an identifier: the repo, the pack ID, and the install and data folders.
- **The install folder isn't the data folder.** Velopack [installs to `%LocalAppData%\<pack ID>`](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/install.rs#L37-L43), and reinstalling deletes that folder. So the pack ID is `OpenBackupManager.App`, which keeps it clear of the app's data in `%LocalAppData%\OpenBackupManager`, and it can't change later without stranding existing installs. The data stays in Local rather than Roaming AppData because it's tied to this device.
- **NativeAOT for all three programs.** They start faster and use less memory than with .NET's runtime, which matters for a tray app and an engine that run all day, and `obm` is one file that can sit alone on PATH.
- **There's no portable build.** Setup already installs per user with no admin rights, which is the usual reason for one, and the app belongs to the system it runs on: it starts at login, registers for notifications, keeps secrets in Credential Manager and keeps its data in `%LocalAppData%`. A portable copy also counts as installed, so it would share the installed copy's home. Velopack's portable zip is turned off with `--noPortable`.

## One copy for each home

- **A copy that wasn't installed has a home of its own,** `%LocalAppData%\OpenBackupManager-Dev`, so a build run while developing never shares data with the installed app. Copies with different homes run side by side, each with its own engine.
- **`AppInstance` to hand over, and the home's lock to cover every session.** The app starts and stops its home's engine, so two copies for one home would fight over it. A new copy first looks for a running one with `AppInstance`, which hands the launch over so the running copy opens its flyout, which a lock can't. `AppInstance` only finds a copy of the same exe, [by its path](https://github.com/microsoft/WindowsAppSDK/blob/v2.6.5/dev/AppLifecycle/Association.cpp#L18-L27), in the same session, so the new copy then takes Core's `ProcessLock`, the engine's own kind of lock, which covers every session and exe.

## The engine

- **Quit stops only an engine the app started,** as quitting OneDrive or Dropbox stops syncing, while one started another way, such as by Task Scheduler at sign-in, keeps running. The engine remembers it was started with `--started-by app` and says so in `status`, so a copy started after the app crashed knows too. A file holding the engine's process ID could be left behind by a crash, and the ID reused.
- **Before an update, the engine is stopped however it was started,** as Velopack then [ends whatever runs from the install folder](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/apply_windows_impl.rs#L151-L159), `obmd` included. An update can also install as the app starts, so Velopack's hook before an update stops it too, though only the engine for the home the app uses without `--home`, as a hook gets only the new version, not the app's arguments. The app starts the new version's engine once it's back.
- **Started as administrator, the app starts itself again without admin rights.** The engine it starts would run as administrator too, which the engine refuses (see [Engine and command line](engine.md)). A process can't give up its admin rights, so `DesktopShell` asks the desktop's Explorer, which runs with the user's normal rights, to start the app, as [Raymond Chen shows](https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643). When Explorer runs as another user, as after Run as different user, or has admin rights too, the app explains instead. On GitHub's runners Explorer belongs to the runner's own account, so CI can only check the explaining.

## Updates

- **Checking for updates by hand is in About, not the tray menu.** The app checks by itself, so it's rarely needed, and About shows the result next to the version instead of in a notification, which still works with notifications turned off.
- **The update notification isn't one that stays on screen until it's clicked.** The update installs on the next restart or Quit anyway. That kind is kept for problems that need the user.

## Tray and flyout

- **WinUIEx for the tray icon.** WinUI has none of its own. WinUIEx's needs no window, takes any WinUI flyout as its menu, and accepts SVG icons. H.NotifyIcon was the alternative, but WinUIEx did everything we needed and its window helpers are useful elsewhere too.
- **The tray icon and flyout follow the taskbar's theme, not the app's.** Windows lets the taskbar (Windows mode) and apps (app mode) differ, and both belong to the taskbar. Windows' own tray flyouts and EarTrumpet's do the same.

## Logs and crash reports

- **Serilog writes the log files.** .NET's logging has no file logger of its own. Serilog's rolls by day and size and keeps a set number of files. NLog does the same but is set up through its own config files, and a logger of our own would leave rolling, locking and flushing to us. Everything else logs through `ILogger`, so only `Program` knows about Serilog.
- **Crashes outside .NET are found from the event log at the next start, not caught as they happen.** Catching them needs a native handler outside the process, such as [Crashpad](https://chromium.googlesource.com/crashpad/crashpad/+/f3769d9630dc028cec188263f860ea80bb0e24c9/doc/overview_design.md#overview), as Chrome and Electron apps use, which is a native part for each OS and usually a server for the dumps. What Windows records is enough to say what failed and where, and Export diagnostics adds the event log entries, as PowerToys' bug report tool does. The `running` file can't tell a power cut from a crash, which is why a report needs Windows' entry.
- **A crash closes the app rather than carrying on.** After an error nothing expected, what the app holds can't be trusted, so it closes and Windows starts it again cleanly.

## Built ourselves

- **Crash reports and the flyout's placement (checked Nov 2026).** The crash reporting packages, such as Sentry, Exceptionless and CrashReporter.NET, send reports to a server, and none finds crashes outside .NET in the event log. H.NotifyIcon [places its popup](https://github.com/HavenDV/H.NotifyIcon/blob/v2.4.1/src/libs/H.NotifyIcon.Shared/TaskbarIcon.Popups.cs#L206-L236) at the cursor or the taskbar's corner rather than at the icon, and WinUIEx places its own flyouts at the icon but doesn't say where the icon is, so a window of our own can't be placed there. Once WinUIEx can ([dotMorten/WinUIEx#281](https://github.com/dotMorten/WinUIEx/issues/281)), `NativeMethods.TrayIconBounds` can go.

## Accepted limits

- **It only builds on Windows.** WinUI's XAML compiler is Windows-only, so other OSes leave out projects with `.Windows` in their names. In WSL, Linux builds can seem to work because WSL runs Windows programs such as the XAML compiler through interop; [turn interop off](https://learn.microsoft.com/en-us/windows/wsl/wsl-config#interop-settings) to test properly.
- **On Windows 10, Mica falls back to a plain background.** Mica is [Windows 11 only](https://learn.microsoft.com/en-us/windows/apps/design/style/mica#usability-and-adaptability).
- **The first time the flyout opens after launch, its slide can stutter.** Warming it up fully would mean activating it at startup, which would take focus from whatever the user is doing.
- **The main window opens without Windows' zoom animation.** Windows plays it when a window is shown, while the window is still blank, and uncloaking doesn't animate.
- **While the main window is dragged or resized, its content trails its frame.** WinUI 3 draws into a swap chain that Windows doesn't keep in step with the frame, a known WinUI flaw, so there's nothing to change on our side ([microsoft/microsoft-ui-xaml#5148](https://github.com/microsoft/microsoft-ui-xaml/issues/5148#issuecomment-855593522)).
- **A silent reinstall from outside the app leaves it closed.** IT tools and other package managers upgrade by running Setup silently, which closes the running app before installing, and after the install hooks closes anything running from the install folder, so the app can't start itself again. [Silent Setup has no option to start it](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/install.rs#L244-L255). It comes back at the next sign-in, as it starts at login.
- **An update leaves the engine as the app's own.** Its engine is stopped however it was started, and the app starts the new version's, so one Task Scheduler started is then stopped by Quit until the next sign-in.
- **A crash before the app has set up its crash handling isn't reported,** such as one while it's choosing its home or building its services. Windows' entry is in the event log, and Windows only restarts a copy that has run for a minute anyway.
- **A hang ended from Task Manager while only the tray icon shows isn't reported.** Windows only records a hang it handles itself, when a window that isn't responding is closed and its prompt's Close the program is chosen. Task Manager ends a background app without any record, so the next start only logs that it closed unexpectedly without saying why.
- **From the hidden icons popup, the flyout opens behind the popup.** The flyout sits just below the taskbar in the stacking order so it slides out from behind it, and the popup sits above the taskbar. OneDrive's flyout does the same.
- **An update can't install while another program has a folder in the install open,** such as File Explorer showing one, or a terminal in one. Velopack replaces the install's `current` folder by renaming it, which Windows refuses then, and only ends programs that run from the install ([velopack/velopack#4](https://github.com/velopack/velopack/issues/4)). `obm`'s folder on PATH is outside `current` for this reason, as VS Code has every folder on PATH open. The app starts again on the version before and says the update wasn't installed, and Try again installs it once that program has let go.
- **Uninstalling ends the engine without stopping it cleanly.** Velopack [ends whatever runs from the install folder](https://github.com/velopack/velopack/blob/1.2.161/src/bins/src/commands/uninstall.rs#L20-L24) before it runs the uninstall hook. An update does the same to engines for other homes, and to `obm` commands, as its hook only knows the home the app uses without `--home`, which is normally the only one the installed app uses.
- **Setup isn't signed, so SmartScreen warns before it runs,** and [Smart App Control](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/overview), when it's on, blocks it. [Signing it](https://docs.velopack.io/packaging/signing), such as with vpk's `--azureTrustedSignFile`, would let Smart App Control run it, though SmartScreen can still warn until the signature builds a reputation.
- **Crash reports from a release name the methods but give no file or line numbers.** Only symbol files have those, and publishing makes none, as NativeAOT's are bigger than the programs.

## Checking by hand

CI can't see display scaling, effects or the taskbar, so changes to the windows or the tray need these checking on a real desktop:

- Light and dark Windows mode, switched while the app is running.
- Animations off, and transparency effects off (Settings > Accessibility > Visual effects).
- 150% scaling on the display with the taskbar, both switched while the app is running and at launch, with another display at a different scaling.
- The tray icon on the taskbar, in the hidden icons popup, and near each end of the taskbar.
- Launching the app again from the Start menu while it's running, with the main window open and closed: the flyout should open and take focus.
- The test notification's name and icon, as it pops up and in the notification centre.
- For changes to text, a dev build run with the `OBM_UI_CULTURE` environment variable set to `qps-ploc`, with each window, the tray menu and the test notification opened. All text should be accented and in brackets: plain text isn't coming from `Strings`, and text that's cut short needs more room.
- For changes to crash handling, a hang: open About, choose Crash > Hang from the tray menu, close About, and choose Close the program on Windows' prompt. The next start should show a report from what Windows recorded. A UI test can't, since the prompt is Windows' own and GitHub's runners keep no record of hangs.
- For changes to starting at login, signing out and in again with it on: the app should be in the tray with no flyout, and turning it off in Settings > Apps > Startup should keep it from starting, even after the app has started again since.
- For changes to starting as administrator, a dev build started with Run as administrator: Task Manager's Details tab should show one copy, with No under Elevated (add the column from its header), and the flyout should open and take focus, the engine connect and the test notification show. Windows Sandbox can't, as its account only has admin rights.
- For changes to installing or updating, a build packed as in CI's package-windows job: a dev build running alongside the installed copy, installing it again over a running copy with `--silent`, and uninstalling it from Settings > Apps.

### Trying updates locally

`tools/Publish-TestUpdate.ps1` packs versions into a local feed in the temp folder, the same way as CI's package-windows job. With `-Install`, it also writes the installed copy's `update-source` file, so it checks that feed however it's started, until it's uninstalled. Installing replaces the installed app and uses its data, so try it in Windows Sandbox or a VM. The script refuses to replace a copy that isn't from a test feed unless given `-Force`.

1. `./tools/Publish-TestUpdate.ps1 -Version 0.0.1-local -Reset -Install` starts a new feed with that version, installs it and starts it. About shows (test updates) after the version.
2. In About, Check for updates should say it's up to date.
3. `./tools/Publish-TestUpdate.ps1 -Version 0.0.2-local` adds a newer version to the feed.
4. Check for updates again: it should download the update, show Version 0.0.2-local is ready to install, and turn the button into Restart to update.
5. Restart to update, or Restart now on the notification, should restart into the new version. To try installing on Quit instead, add another version as in step 3, check, and quit.
6. Uninstall Open Backup Manager from Settings > Apps when done.

## Releasing

1. Push a tag such as `v0.1.0`, or `v0.1.0-alpha.1` for a prerelease, following [Versions](#versions).
2. CI's package-windows job checks the tag, then builds x64 and ARM64, packs each with Velopack and attests them. The other package jobs do the same for the macOS and Linux tarballs, the `.deb` and `.rpm`, and the Docker image, which package-docker pushes (see [Engine and command line](engine.md)). Once all of them and the install tests have finished, the release job uploads everything to a draft release called `v0.1.0`, marked as a prerelease if the version has a `-`.
3. Write the notes and publish the draft. Installed copies only see published releases, so nobody updates until then.

The repository makes published releases immutable, so a broken release is replaced by a new version, never patched, and a release job can only be re-run before publishing. A re-run fails on what the first run uploaded, so delete the draft release first, keeping its tag.

To try releasing itself, tag throwaway prereleases such as `v0.0.2-test.1` and `v0.0.2-test.2`, with a version number never used before, since an old tag name may not be reusable and copies that installed it wouldn't update to it again. Delete the releases and their tags afterwards.

### Versions

A release's version comes from its tag alone, so nothing in the repository is bumped by hand. `tools/Get-Version.ps1` works it out for CI.

- A tag is `vX.Y.Z`, or `vX.Y.Z-label.N` for a prerelease, with a lowercase label and its number after a dot, such as `alpha.1`, `beta.2` or `rc.1`. Versions sort by semver's rules, where `beta10` would come before `beta9` and `Beta` before `alpha`, so CI refuses other forms.
- Each tag must be higher than every version tag before it, as installed copies only update to a higher version, and must be on `main`. CI refuses one that isn't. To undo a release, release the code you want as a new version. The repository's Version tags ruleset lets only admins create, move or delete `v*` tags.

### Edge builds

Every run except a tag's, pull requests included, packs both architectures as for a release and uploads Setup.exe and the full package, and the macOS and Linux tarballs, to the run as `OpenBackupManager-edge-<version>-<runtime>`. After `v0.1.0`, edge builds are `0.1.1-alpha.0.<commits since it>`, and after `v0.1.0-beta.2`, `0.1.0-beta.2.<commits since it>`. So an installed edge build sorts after the release it follows and before any that can come next, which it then updates to.
