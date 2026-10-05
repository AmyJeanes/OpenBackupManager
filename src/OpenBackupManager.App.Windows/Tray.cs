using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class Tray : IDisposable
{
    private readonly TrayIcon _icon;
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Action _open;
    private readonly Action _quit;

    public Tray(Action open, Action quit)
    {
        _open = open;
        _quit = quit;
        _icon = new TrayIcon(1, IconPath(State), Tooltip(State));
        _icon.Selected += (_, _) => _open();
        _icon.ContextMenu += (_, e) => e.Flyout = Menu();
        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
        _icon.IsVisible = true;
    }

    public TrayState State
    {
        get;
        set
        {
            field = value;
            UpdateIcon();
        }
    }

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _icon.Dispose();
    }

    private void OnColorValuesChanged(UISettings sender, object args) => _dispatcher.TryEnqueue(UpdateIcon);

    private void UpdateIcon()
    {
        _icon.SetIcon(IconPath(State));
        // WinUIEx 2.9.3 hides the tooltip whenever the icon changes, and only sends a tooltip that's different.
        // Remove once the fix for https://github.com/dotMorten/WinUIEx/issues/279 is released
        _icon.Tooltip = string.Empty;
        _icon.Tooltip = Tooltip(State);
    }

    private MenuFlyout Menu()
    {
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = "Sync all now", IsEnabled = false });
        menu.Items.Add(new MenuFlyoutItem { Text = "Pause", IsEnabled = false });
        menu.Items.Add(Item("Open", _open));
#if DEBUG
        var states = new MenuFlyoutSubItem { Text = "Icon state" };
        foreach (var state in Enum.GetValues<TrayState>())
        {
            states.Items.Add(Item(state.ToString(), () => State = state));
        }

        menu.Items.Add(states);
#endif
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Quit", _quit));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static string IconPath(TrayState state) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", TaskbarIsLight() ? "Light" : "Dark", $"{state}.svg");

    // The taskbar follows the Windows mode, which can differ from the app mode
    private static bool TaskbarIsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    private static string Tooltip(TrayState state) => "OpenBackupManager\n" + state switch
    {
        TrayState.Idle => "Up to date",
        TrayState.Syncing => "Syncing",
        TrayState.Paused => "Paused",
        TrayState.NeedsAttention => "Needs attention",
        TrayState.Error => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
