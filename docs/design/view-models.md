# View models

What each screen shows and does lives in `src/OpenBackupManager.ViewModels`, with no UI framework, so every app draws the same screens and their behaviour is tested on every OS.

## How it works

- Each screen has a view model: its text and state as properties, and what it can do as commands. The app's view binds to them and keeps only what belongs to its platform, such as window size, icons and effects. Text that changes with state comes from the view model, and fixed labels bind straight to `Strings` in the views.
- Text people see lives in `Strings.resx`, in British English, and is read through the `Strings` class generated from it when building. `Text.Format` fills in placeholders in the user's own formats, and a comment says what a placeholder holds when the text doesn't make it clear. All the apps' text is there, including the Windows tray menu and notifications. The exception is what Core says about problems in `config.toml`, which is in Core's own `Strings.resx`, since Core finds them and can't use the view models. The display name is never translated, so text that mentions it has a placeholder for it. A test fails if the Windows app's XAML has text of its own.
- Debug builds also get a `qps-ploc` pseudo-locale, which `src/PseudoLocale.targets` generates from each `Strings.resx`: every letter accented and the text 40% longer, in brackets. Setting the `OBM_UI_CULTURE` environment variable to `qps-ploc` shows it, which finds text that doesn't come from `Strings` and text that gets cut off.
- They're built with CommunityToolkit.Mvvm. `[ObservableProperty]` generates properties that tell the view when they change, and `[RelayCommand]` generates commands. A command made from an async method reports while it's running, so its button is disabled until it finishes without the view doing anything.
- What a view model needs from the app comes through interfaces defined here, which each app implements: `IUpdater` for updates, and `IAppActions` for what the app does, such as opening a window, quitting or restarting to update. `AppNotifications` builds the app's notifications from `Strings` and shows them through Core's `INotifier` (see [Platform layer](platform.md)), and when one is clicked, it does what the click asked for through `IAppActions`. `AppInfo` carries the app's name, version, copyright and repository, read from the exe's attributes, which come from `Product`, `Company` and `RepositoryUrl` in `Directory.Build.props`, so each is set in one place. About's links are built from the repository, as are the updater's GitHub source and release notes links. Each app registers its implementations and view models with its services, and a window gets its view model from them.
- They read the time from `TimeProvider`, so tests can set the clock and time zone and move time on.
- The Windows app binds with `x:Bind`, which is checked when it builds. Its controls keep their `x:Name`s, which the UI tests find them by.

## Why

- **The pseudo-locale is generated, not written.** It's made from `Strings.resx` on every Debug build, so it never falls behind the English or ships in a release, and the app finds it the same way it will find a translation.
- **XAML binds to `Strings` with `x:Bind`, rather than through a markup extension.** A missing or mistyped key then fails the build, where a markup extension's string keys only fail when the window opens. WinUI's own `.resw` files and `x:Uid` only work on Windows.
- **CommunityToolkit.Mvvm.** It's Microsoft's MVVM library, works with any UI framework or none, and its generators write the property and command code at build time without reflection. ReactiveUI and Prism are larger frameworks with their own way of structuring an app, which we don't need.
- **Interfaces for what the app provides, rather than moving it here.** The updater is tied to Windows notifications and Velopack's setup, so it stays in the Windows app and About's behaviour is still shared and tested.
