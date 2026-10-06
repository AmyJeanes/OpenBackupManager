# Windows app

The Windows app is a native WinUI 3 tray app, in `src/OpenBackupManager.App.Windows`.

## How it works

- It's unpackaged (a plain folder with an exe, no MSIX) and carries its own copy of the Windows App SDK, so nothing else needs installing. It runs on Windows 10 1809 or later.
- The main window uses the Mica backdrop and extends its content into the title bar.
- `Tray` puts the icon in the notification area through WinUIEx's `TrayIcon`. Left-click opens the window, and right-click opens a menu with Sync all now, Pause, Open and Quit. Debug builds also get an Icon state submenu for trying each status icon.
- The app icon, `Assets/AppIcon.ico`, is used for the exe, the window and its taskbar button. It's full colour, so one version has to read on both light and dark taskbars.
- The tray icon is single-colour and shows one of five states: idle, syncing, paused, needs attention and error. Each state has an SVG for a light taskbar and one for a dark taskbar in `Assets/Tray/`, and the icon switches when the Windows mode changes.
- Closing the window destroys it, and the tray keeps the app running. Quit removes the tray icon and exits.

## Why

- **Only the WinUI package, not all of `Microsoft.WindowsAppSDK`.** The full package also brings the SDK's AI, machine learning and search parts, which take the app from 94 MB to 154 MB. `Microsoft.WindowsAppSDK.InteractiveExperiences` is referenced directly because the WinUI package asks for a version of it that was never published.
- **WinUIEx for the tray icon.** WinUI has none of its own. WinUIEx's needs no window, takes any WinUI flyout as its menu, and accepts SVG icons. H.NotifyIcon was the alternative, but WinUIEx did everything we needed and its window helpers are useful elsewhere too.
- **CsWin32 for the Windows APIs that WinUI and WinUIEx don't cover.** It generates the declarations, structs and constants from Windows' own metadata for the names in `NativeMethods.txt`, which is Microsoft's recommended way to call them, so `NativeMethods.cs` only holds our helpers.
- **Windows are destroyed when closed, not hidden,** so the app stays small while it sits in the tray.
- **The tray icon follows the taskbar's theme, not the app's.** Windows lets the taskbar (Windows mode) and apps (app mode) differ, and the tray icon sits on the taskbar.
- **Windows stay cloaked until they're drawn.** A newly shown window is blank until WinUI's first frame reaches the screen, and the title bar lays itself out again a frame later. A cloaked window is still drawn but not put on screen, so `FirstDraw` keeps a new window cloaked until its layout has settled and Windows has shown that frame.

## Accepted limits

- **It only builds on Windows.** WinUI's XAML compiler is Windows-only, so `Directory.Solution.targets` leaves out projects with `.Windows` in their names on other OSes, and CI's format check and CodeQL run on Windows. In WSL, Linux builds can seem to work because WSL runs Windows programs such as the XAML compiler through interop; turn interop off to test properly.
- **IntelliSense outside Visual Studio needs a workaround.** C# Dev Kit's background build skips WinUI's XAML step, so the project adds it back until [microsoft/vscode-dotnettools#2990](https://github.com/microsoft/vscode-dotnettools/issues/2990) is fixed. It relies on an internal WinUI target name, so a WinUI update could break the build until the line is updated.
- **On Windows 10, Mica falls back to a plain background.** Mica is Windows 11 only.
- **The main window opens without Windows' zoom animation.** Windows plays it when a window is shown, while the window is still blank, and uncloaking doesn't animate.
