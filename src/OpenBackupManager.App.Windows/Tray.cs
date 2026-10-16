using System.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using OpenBackupManager.ViewModels;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class Tray : IDisposable
{
    private const uint IconId = 1;

    private readonly TrayIcon _icon;
    private readonly UISettings _uiSettings = new();
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    private readonly StatusViewModel _status;
    private readonly IAppActions _actions;
    private readonly AppNotifications _notifications;
    private bool _skipClick;

    public Tray(StatusViewModel status, IAppActions actions, AppNotifications notifications)
    {
        _status = status;
        _actions = actions;
        _notifications = notifications;
        Flyout = new StatusFlyout(status, IconId);
        Flyout.Update(TaskbarIsLight());
        _icon = new TrayIcon(IconId, IconPath(status.State), Tooltip());
        _icon.Selected += (_, _) => OnClick();
        _icon.LeftDoubleClick += (_, _) => OnDoubleClick();
        _icon.ContextMenu += (_, e) => e.Flyout = Menu();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        _status.PropertyChanged += OnStatusChanged;
        _icon.IsVisible = true;
    }

    // Open for as long as the app runs, though hidden while it isn't shown
    public StatusFlyout Flyout { get; }

    public void ShowFlyout() => Flyout.Show();

    private void OnClick()
    {
        if (_skipClick)
        {
            _skipClick = false;
            return;
        }

        // Clicking the icon while the flyout is open should only close it. Pressing the icon gives the taskbar focus,
        // which hides the flyout, and the click then arrives on release, so a click just after it hides is that one
        if (!Flyout.JustHidden)
        {
            ShowFlyout();
        }
    }

    // The first click has already opened the flyout, which closes when the window takes focus.
    // The second click arrives as a double-click and then a click, and that click shouldn't reopen the flyout
    private void OnDoubleClick()
    {
        _skipClick = true;
        _actions.Open();
    }

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _status.PropertyChanged -= OnStatusChanged;
        _icon.Dispose();
        Flyout.Close();
    }

    private void OnColorValuesChanged(UISettings sender, object args) => _ui.Post(_ => Update(), null);

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StatusViewModel.State) or nameof(StatusViewModel.StatusText))
        {
            Update();
        }
    }

    private void Update()
    {
        Flyout.Update(TaskbarIsLight());
        _icon.SetIcon(IconPath(_status.State));
        // WinUIEx 2.9.3 hides the tooltip whenever the icon changes, and only sends a tooltip that's different.
        // Remove once the fix for https://github.com/dotMorten/WinUIEx/issues/279 is released
        _icon.Tooltip = string.Empty;
        _icon.Tooltip = Tooltip();
    }

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = Strings.Tray_SyncAllNow, IsEnabled = false });
        menu.Items.Add(new MenuFlyoutItem { Text = Strings.Tray_Pause, IsEnabled = false });
        menu.Items.Add(Item(Strings.Tray_Open, _actions.Open));
        menu.Items.Add(Item(Strings.Tray_ShowTestNotification, _notifications.ShowTest));
#if DEBUG
        var states = new MenuFlyoutSubItem { Text = "Icon state" };
        foreach (var state in Enum.GetValues<SyncState>())
        {
            states.Items.Add(Item(state.ToString(), () => _status.State = state));
        }

        menu.Items.Add(states);
        menu.Items.Add(new MenuFlyoutSubItem
        {
            Text = "Crash",
            Items =
            {
                // Queued, as async code resumes, rather than thrown by the item, which UI Automation's invoke would catch
                Item("On the UI thread", () => _ui.Post(_ => throw new InvalidOperationException("Test crash on the UI thread"), null)),
                Item("In a click", () => throw new InvalidOperationException("Test crash in a click")),
                Item("On another thread", () => new Thread(() => throw new InvalidOperationException("Test crash on another thread")).Start()),
                // Ends the app at once without running any handler, as a crash inside Windows' own code does
                Item("Fail fast", () => Environment.FailFast("Test crash that fails fast")),
                // Queued, so the click finishes before the UI thread stops responding
                Item("Hang", () => _ui.Post(_ => Thread.Sleep(Timeout.Infinite), null)),
            },
        });
#endif
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(Strings.Tray_About, _actions.About));
        menu.Items.Add(Item(Strings.Tray_Quit, _actions.Quit));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static string IconPath(SyncState state) => IconPath(state, TaskbarIsLight());

    internal static string IconPath(SyncState state, bool light) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", light ? "Light" : "Dark", $"{state}.svg");

    // The taskbar follows the Windows mode, which can differ from the app mode
    private static bool TaskbarIsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    private string Tooltip() => _status.AppName + "\n" + _status.StatusText;
}
