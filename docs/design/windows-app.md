# Windows app

The Windows app is a native WinUI 3 tray app, in `src/OpenBackupManager.App.Windows`.

## How it works

- It's unpackaged (a plain folder with an exe, no MSIX) and carries its own copy of the Windows App SDK, so nothing else needs installing. It runs on Windows 10 1809 or later.
- The main window uses the Mica backdrop and extends its content into the title bar.
- `Tray` puts the icon in the notification area through WinUIEx's `TrayIcon`. Left-click opens the status flyout, double-click opens the main window, and right-click opens a menu with Sync all now, Pause, Open, Show test notification, About and Quit. Debug builds also get an Icon state submenu for trying each status icon.
- The status flyout, `StatusFlyout`, is a small borderless window with the status, Sync now and Open. It opens centred on the tray icon, just clear of the taskbar, or just clear of the icon in the hidden icons popup. It slides out from behind the taskbar, follows the Windows mode like the tray icon, and closes when it loses focus or on Esc.
- The app icon, `Assets/AppIcon.ico`, is used for the exe, the window and its taskbar button. It's full colour, so one version has to read on both light and dark taskbars. `Assets/AppIcon.png` is its 256-pixel size, for showing it larger in the app, since an image control only shows an `.ico`'s first, smallest size.
- The About window shows the version and links to the repo, licence and third-party notices. It also shows when updates were last checked, with a button that checks now, or restarts to install an update that's waiting. In a dev build the button is greyed out, so the layout matches.
- The tray icon is single-colour and shows one of five states: idle, syncing, paused, needs attention and error. Each state has an SVG for a light taskbar and one for a dark taskbar in `Assets/Tray/`, and the icon switches when the Windows mode changes.
- Launching the app opens the status flyout rather than the main window, which is the full view and opens from Open. Closing the window destroys it, and the tray keeps the app running. Quit removes the tray icon and exits.
- One copy runs for each home folder. Launching the app again hands over to the running copy, which opens its flyout, and the new copy exits.
- `AppHome` picks the home folder, where everything the app keeps will go: `--home`, then the `OBM_HOME` environment variable, then `%LocalAppData%\OpenBackupManager` for an installed copy, or `%LocalAppData%\OpenBackupManager-Dev` for one that wasn't installed, such as a build run while developing. The single-instance lock is named from it, so copies with different homes run side by side. A copy that wasn't installed calls itself "OpenBackupManager (dev)" in its tooltip, flyout, window title and notifications.
- Velopack installs it per user into `%LocalAppData%\OpenBackupManager.App`, with a Start menu shortcut and no admin rights. Each architecture, x64 and ARM64, is its own Velopack channel, so an install only updates to its own build.
- `Updater` checks GitHub Releases when the app starts and every 6 hours, and downloads an update in the background. It then shows an Update available notification whose Restart now installs it, or it installs when the user quits or next starts the app. The first time a newer version starts, however it was installed, an Update installed notification links to its release notes. It knows by keeping the last version that ran in `last-version` in the home folder. It follows prereleases while running one, until Settings has an option for it. Copies that weren't installed by Velopack don't check. For trying updates out locally, an `update-source` file in the install folder, or the `OBM_UPDATE_SOURCE` environment variable, points it at a folder or URL instead of GitHub, and About marks the version with (test updates). The file lasts through updates but goes when the app is uninstalled or reinstalled.
- Uninstalling runs the app's uninstall hook first, which removes its notification registration.
- `Notifications` shows Windows notifications through the Windows App SDK's `AppNotificationManager`. Clicking one or its buttons comes back to the running app, or starts it if it isn't running. Started that way, it does what the click asked for, such as opening the main window, and doesn't open the flyout. For now the tray menu has Show test notification, a placeholder sync failure whose View details button opens the main window, as does clicking the notification itself.
- `App.Windows.UITests` starts the built app with a temporary `--home`, so it runs beside any other copy, and drives it through UI Automation with FlaUI. Windows won't let a background process bring a window to the front unless that process had the last keyboard or mouse input, so the tests press Alt themselves first. The normal app doesn't need this: the click or launch that opens it gives it that permission. GitHub's Windows runners start with windows that won't give up the foreground, so CI closes them first with `.github/scripts/Clear-Desktop.ps1`.

## Why

- **Only the WinUI package, not all of `Microsoft.WindowsAppSDK`.** The full package also brings the SDK's AI, machine learning and search parts, which take the app from 94 MB to 154 MB. `Microsoft.WindowsAppSDK.InteractiveExperiences` is referenced directly because the WinUI package asks for a version of it that was never published.
- **WinUIEx for the tray icon.** WinUI has none of its own. WinUIEx's needs no window, takes any WinUI flyout as its menu, and accepts SVG icons. H.NotifyIcon was the alternative, but WinUIEx did everything we needed and its window helpers are useful elsewhere too.
- **CsWin32 for the Windows APIs that WinUI and WinUIEx don't cover.** It generates the declarations, structs and constants from Windows' own metadata for the names in `NativeMethods.txt`, which is Microsoft's recommended way to call them, so `NativeMethods.cs` only holds our helpers.
- **A double-click doesn't wait to rule out a single click.** Waiting for the double-click time (500 ms by default) would slow every flyout open, which otherwise takes about 20 ms. The first click opens the flyout, and the double-click opens the main window, which takes focus and closes it.
- **One copy through the Windows App SDK's `AppInstance`, not a named mutex.** A mutex only tells the new copy that another is running, while `AppInstance` also hands the launch over so the running copy can open its flyout. `Program.cs` replaces WinUI's generated `Main` so the check happens before anything is created. The new copy has to pass its right to take focus to the running copy, since Windows only gives it to the app the user just launched.
- **Notifications are registered with the app's name and icon.** Without them, Windows labels them with the exe's name, OpenBackupManager.App.Windows. The click handler is attached before registering, or Windows starts another copy of the app for each click.
- **Notifications are made with the Windows App SDK's builder.** It escapes text such as folder names, but not button labels ([microsoft/WindowsAppSDK#4996](https://github.com/microsoft/WindowsAppSDK/issues/4996)), so `Notifications` escapes those itself. Its buttons can only call back into the app, which would start it again after Quit, so `WithDismissButton` adds Windows' own dismiss button, such as the update notification's Not now, to the XML the builder makes ([microsoft/WindowsAppSDK#2960](https://github.com/microsoft/WindowsAppSDK/issues/2960)).
- **The update notification isn't one that stays on screen until it's clicked.** The update installs on the next restart or Quit anyway. That kind is kept for problems that need the user.
- **The install folder isn't the data folder.** Velopack always installs to `%LocalAppData%\<pack ID>` unless an install is told otherwise, and reinstalling, as a winget upgrade does, deletes that folder. So the pack ID is `OpenBackupManager.App`, which keeps it clear of the app's data in `%LocalAppData%\OpenBackupManager`. The data stays in Local rather than Roaming AppData because it's tied to this device. The pack ID can't change later without stranding existing installs.
- **Checking for updates by hand is in About, not the tray menu.** The app checks by itself, so it's rarely needed, and About shows the result next to the version instead of in a notification, which still works with notifications turned off.
- **Self-update stays on for winget installs.** winget runs the same Setup, and Velopack keeps the version in the uninstall entry current as it updates, so once the app has updated itself, winget sees it as up to date.
- **There's no portable build.** Setup already installs per user with no admin rights, which is the usual reason for one, and the app belongs to the system it runs on: it starts at login, registers for notifications, keeps secrets in Credential Manager and keeps its data in `%LocalAppData%`. A portable copy also counts as installed, so it would share the installed copy's home. Velopack's portable zip is turned off with `--noPortable`.
- **Windows are destroyed when closed, not hidden,** so the app stays small while it sits in the tray.
- **The tray icon and flyout follow the taskbar's theme, not the app's.** Windows lets the taskbar (Windows mode) and apps (app mode) differ, and both belong to the taskbar. Windows' own tray flyouts and EarTrumpet's do the same.
- **The flyout uses Windows' blur with one colour over it, not WinUI's acrylic.** WinUI's acrylic adds a luminosity layer that turns dark backgrounds solid black or makes light ones too pale. The colours (`#232323` at 77% for dark, `#E3E3E3` at 86% for light) were measured from EarTrumpet's flyout over black and white, which reads well on any background.
- **Windows stay cloaked until they're drawn.** A newly shown window is blank until WinUI's first frame reaches the screen, and the title bar lays itself out again a frame later. A cloaked window is still drawn but not put on screen, so `FirstDraw` keeps a new window cloaked until its layout has settled and Windows has shown that frame. The flyout is created once and cloaked again when it closes, so later opens are instant, and status and theme changes are applied while it's cloaked.
- **The flyout finds the tray icon by asking with each of the app's windows.** WinUIEx doesn't expose the window its icon belongs to, which `Shell_NotifyIconGetRect` needs. Using the icon's position rather than the pointer's keeps the flyout in place when the icon is opened from the keyboard. If the icon can't be found, the flyout opens in the corner by the notification area.
- **The flyout is sized for the icon's display.** Its sizes are in device-independent pixels and converted with that display's scaling. It's placed on that display before it's first drawn, and if Windows rescales it after a move between displays with different scaling, it puts the size back.

## Accepted limits

- **It only builds on Windows.** WinUI's XAML compiler is Windows-only, so `Directory.Solution.targets` leaves out projects with `.Windows` in their names on other OSes, and CI's format check and CodeQL run on Windows. In WSL, Linux builds can seem to work because WSL runs Windows programs such as the XAML compiler through interop; turn interop off to test properly.
- **IntelliSense outside Visual Studio needs a workaround.** C# Dev Kit's background build skips WinUI's XAML step, so the project adds it back until [microsoft/vscode-dotnettools#2990](https://github.com/microsoft/vscode-dotnettools/issues/2990) is fixed. It relies on an internal WinUI target name, so a WinUI update could break the build until the line is updated.
- **On Windows 10, Mica falls back to a plain background.** Mica is Windows 11 only.
- **The first time the flyout opens after launch, its slide can stutter.** Warming it up fully would mean activating it at startup, which would take focus from whatever the user is doing.
- **The main window opens without Windows' zoom animation.** Windows plays it when a window is shown, while the window is still blank, and uncloaking doesn't animate.
- **Dragging the main window lags a little behind the mouse.** WinUI 3 draws the window's content separately from Windows drawing its frame, so they don't move in step. Other WinUI 3 apps such as PowerToys Settings do the same, and there's nothing to change on our side ([microsoft/microsoft-ui-xaml#11096](https://github.com/microsoft/microsoft-ui-xaml/discussions/11096)).
- **The build takes one DLL from the Windows App SDK runtime's MSIX.** Registering for notifications loads `Microsoft.WindowsAppRuntime.Insights.Resource.dll`, which is only shipped inside that MSIX, so a self-contained app fails without it ([microsoft/WindowsAppSDK#6774](https://github.com/microsoft/WindowsAppSDK/issues/6774)). The project unzips it from the `Microsoft.WindowsAppSDK.Runtime` package until a fixed release is out.
- **A silent reinstall from outside the app leaves it closed.** IT tools and other package managers upgrade by running Setup silently, which closes the running app before installing, and after the install hooks closes anything running from the install folder, so the app can't start itself again. Silent Setup has no option to start it. winget won't do this: its manifest will set `UpgradeBehavior: deny`, so winget and tools built on it refuse to upgrade the app, and `RequireExplicitUpgrade: true`, so `winget upgrade --all` skips it instead of reporting the refusal as a failure. Once the app starts at login, it comes back at the next sign-in.
- **From the hidden icons popup, the flyout opens behind the popup.** The flyout sits just below the taskbar in the stacking order so it slides out from behind it, and the popup sits above the taskbar. OneDrive's flyout does the same.

## Checking by hand

CI can't see display scaling, effects or the taskbar, so changes to the windows or the tray need these checking on a real desktop:

- Light and dark Windows mode, switched while the app is running.
- Animations off, and transparency effects off (Settings > Accessibility > Visual effects).
- 150% scaling on the display with the taskbar, both switched while the app is running and at launch, with another display at a different scaling.
- The tray icon on the taskbar, in the hidden icons popup, and near each end of the taskbar.
- Clicking the icon while the flyout is open, Esc, double-click, and opening the main window from the flyout.
- Launching the app again from the Start menu while it's running, with the main window open and closed: the flyout should open and take focus.
- The test notification's name and icon, View details, and View details from the notification centre after Quit, which should start the app with the main window and no flyout.
- For changes to installing or updating, a build packed as in CI's release job: installing it, a dev build running alongside the installed copy, installing it again over a running copy with `--silent`, and uninstalling it from Settings > Apps.

### Trying updates locally

`tools/Publish-TestUpdate.ps1` packs versions into a local feed in the temp folder, the same way as CI's release job. With `-Install`, it also writes the installed copy's `update-source` file, so it checks that feed however it's started, until it's uninstalled.

1. `./tools/Publish-TestUpdate.ps1 -Version 0.0.1-local -Reset -Install` starts a new feed with that version, installs it and starts it. About shows (test updates) after the version.
2. In About, Check for updates should say it's up to date.
3. `./tools/Publish-TestUpdate.ps1 -Version 0.0.2-local` adds a newer version to the feed.
4. Check for updates again: it should download the update, show Version 0.0.2-local is ready to install, and turn the button into Restart to update.
5. Restart to update, or Restart now on the notification, should restart into the new version. To try installing on Quit instead, add another version as in step 3, check, and quit.
6. Uninstall OpenBackupManager from Settings > Apps when done.

## Releasing

1. Push a tag such as `v0.1.0`, or `v0.1.0-alpha.1` for a prerelease.
2. CI's release job builds x64 and ARM64, packs each with Velopack, and uploads them to a draft release called `Release v0.1.0`, marked as a prerelease if the version has a `-`.
3. Write the notes and publish the draft. Installed copies only see published releases, so nobody updates until then.

The repository makes published releases immutable, so a broken release is replaced by a new version, never patched, and a release job can only be re-run before publishing.

To try the release job itself, tag throwaway prereleases such as `v0.0.2-test.1` and `v0.0.2-test.2`, with a version number never used before, since an old tag name may not be reusable and copies that installed it wouldn't update to it again. Delete the releases and their tags afterwards.
