# View models

What each screen shows and does lives in `OpenBackupManager.ViewModels`, with no UI framework, so every app draws the same screens and their behaviour is tested on every OS. How to add text people see is in [CONTRIBUTING](../../CONTRIBUTING.md).

## Code map

- `OpenBackupManager.ViewModels`: a view model for each screen, such as `StatusViewModel` and `AboutViewModel`, `AppNotifications` for what the app's notifications say and what clicking them does, `AppInfo`, and `Strings.resx`, which has all the apps' text. `IAppActions` and `IUpdater` are what each app provides.
- `src/PseudoLocale.targets` makes the `qps-ploc` pseudo-locale for Debug builds.
- `OpenBackupManager.ViewModels.Tests` tests them on every OS.

## Views and view models

- **The view keeps only what belongs to its platform,** such as window size, icons and effects. Text that changes with state comes from the view model, and fixed labels bind straight to `Strings`.
- **[CommunityToolkit.Mvvm](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/).** It's Microsoft's MVVM library, works with any UI framework or none, and its generators write the property and command code at build time. ReactiveUI and Prism are larger frameworks with their own way of structuring an app, which we don't need.
- **Interfaces for what the app provides, rather than moving it here.** The updater is tied to Windows notifications and Velopack's setup, so it stays in the Windows app and About's behaviour is still shared and tested.

## Text

- **Text is in British English, and the app's name is never translated,** so text that mentions it has a placeholder for it.
- **The pseudo-locale is generated, not written.** It's made from `Strings.resx` on every Debug build, so it never falls behind the English or ships in a release, and the app finds it the same way it will find a translation.
- **XAML binds to `Strings` with `x:Bind`, rather than through a markup extension.** A missing or mistyped key then [fails the build](https://learn.microsoft.com/en-us/windows/apps/develop/platform/xaml/x-bind-markup-extension), where a markup extension's string keys only fail when the window opens. WinUI's own `.resw` files and `x:Uid` only work on Windows.
