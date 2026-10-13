using System.ComponentModel;
using Microsoft.UI.Dispatching;
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
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly StatusViewModel _status;
    private readonly IAppActions _actions;
    private readonly Notifications _notifications;
    private readonly StatusFlyout _flyout;
    private bool _skipClick;

    public Tray(StatusViewModel status, IAppActions actions, Notifications notifications)
    {
        _status = status;
        _actions = actions;
        _notifications = notifications;
        _flyout = new StatusFlyout(status, IconId);
        _flyout.Update(TaskbarIsLight());
        _icon = new TrayIcon(IconId, IconPath(status.State), Tooltip());
        _icon.Selected += (_, _) => OnClick();
        _icon.LeftDoubleClick += (_, _) => OnDoubleClick();
        _icon.ContextMenu += (_, e) => e.Flyout = Menu();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        _status.PropertyChanged += OnStatusChanged;
        _icon.IsVisible = true;
    }

    public void ShowFlyout() => _flyout.Show();

    private void OnClick()
    {
        if (_skipClick)
        {
            _skipClick = false;
            return;
        }

        // Clicking the icon while the flyout is open should only close it. Pressing the icon gives the taskbar focus,
        // which hides the flyout, and the click then arrives on release, so a click just after it hides is that one
        if (!_flyout.JustHidden)
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
        _flyout.Close();
    }

    private void OnColorValuesChanged(UISettings sender, object args) => _dispatcher.TryEnqueue(Update);

    private void OnStatusChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StatusViewModel.State))
        {
            Update();
        }
    }

    private void Update()
    {
        _flyout.Update(TaskbarIsLight());
        _icon.SetIcon(IconPath(_status.State));
        // WinUIEx 2.9.3 hides the tooltip whenever the icon changes, and only sends a tooltip that's different.
        // Remove once the fix for https://github.com/dotMorten/WinUIEx/issues/279 is released
        _icon.Tooltip = string.Empty;
        _icon.Tooltip = Tooltip();
    }

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Sync all now", IsEnabled = false });
        menu.Items.Add(new MenuFlyoutItem { Text = "Pause", IsEnabled = false });
        menu.Items.Add(Item("Open", _actions.Open));
        menu.Items.Add(Item("Show test notification", _notifications.ShowTest));
#if DEBUG
        var states = new MenuFlyoutSubItem { Text = "Icon state" };
        foreach (var state in Enum.GetValues<SyncState>())
        {
            states.Items.Add(Item(state.ToString(), () => _status.State = state));
        }

        menu.Items.Add(states);
#endif
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("About", _actions.About));
        menu.Items.Add(Item("Quit", _actions.Quit));
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
