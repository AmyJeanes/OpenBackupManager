# Windows app

The Windows app is a native WinUI 3 tray app, in `src/OpenBackupManager.App.Windows`.

## How it works

- It's unpackaged (a plain folder with an exe, no MSIX) and carries its own copy of the Windows App SDK, so nothing else needs installing. It runs on Windows 10 1809 or later.
- The main window uses the Mica backdrop and extends its content into the title bar.
- `Tray` puts the icon in the notification area through WinUIEx's `TrayIcon`. Left-click opens the status flyout, double-click opens the main window, and right-click opens a menu with Sync all now, Pause, Open and Quit. Debug builds also get an Icon state submenu for trying each status icon.
- The status flyout, `StatusFlyout`, is a small borderless window with the status, Sync now and Open. It opens centred on the tray icon, just clear of the taskbar, or just clear of the icon in the hidden icons popup. It slides out from behind the taskbar, follows the Windows mode like the tray icon, and closes when it loses focus or on Esc.
- The app icon, `Assets/AppIcon.ico`, is used for the exe, the window and its taskbar button. It's full colour, so one version has to read on both light and dark taskbars.
- The tray icon is single-colour and shows one of five states: idle, syncing, paused, needs attention and error. Each state has an SVG for a light taskbar and one for a dark taskbar in `Assets/Tray/`, and the icon switches when the Windows mode changes.
- Launching the app opens the status flyout rather than the main window, which is the full view and opens from Open. Closing the window destroys it, and the tray keeps the app running. Quit removes the tray icon and exits.

## Why

- **Only the WinUI package, not all of `Microsoft.WindowsAppSDK`.** The full package also brings the SDK's AI, machine learning and search parts, which take the app from 94 MB to 154 MB. `Microsoft.WindowsAppSDK.InteractiveExperiences` is referenced directly because the WinUI package asks for a version of it that was never published.
- **WinUIEx for the tray icon.** WinUI has none of its own. WinUIEx's needs no window, takes any WinUI flyout as its menu, and accepts SVG icons. H.NotifyIcon was the alternative, but WinUIEx did everything we needed and its window helpers are useful elsewhere too.
- **CsWin32 for the Windows APIs that WinUI and WinUIEx don't cover.** It generates the declarations, structs and constants from Windows' own metadata for the names in `NativeMethods.txt`, which is Microsoft's recommended way to call them, so `NativeMethods.cs` only holds our helpers.
- **A double-click doesn't wait to rule out a single click.** Waiting for the double-click time (500 ms by default) would slow every flyout open, which otherwise takes about 20 ms. The first click opens the flyout, and the double-click opens the main window, which takes focus and closes it.
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
- **From the hidden icons popup, the flyout opens behind the popup.** The flyout sits just below the taskbar in the stacking order so it slides out from behind it, and the popup sits above the taskbar. OneDrive's flyout does the same.

## Checking by hand

CI can't see display scaling, effects or the taskbar, so changes to the windows or the tray need these checking on a real desktop:

- Light and dark Windows mode, switched while the app is running.
- Animations off, and transparency effects off (Settings > Accessibility > Visual effects).
- 150% scaling on the display with the taskbar, both switched while the app is running and at launch, with another display at a different scaling.
- The tray icon on the taskbar, in the hidden icons popup, and near each end of the taskbar.
- Clicking the icon while the flyout is open, Esc, double-click, and opening the main window from the flyout.
