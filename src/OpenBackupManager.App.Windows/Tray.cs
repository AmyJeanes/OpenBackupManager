using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Windows.UI.ViewManagement;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class Tray : IDisposable
{
    private const uint IconId = 1;

    private readonly TrayIcon _icon;
    private readonly UISettings _uiSettings = new();
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private readonly Action _open;
    private readonly Action _about;
    private readonly Action _quit;
    private readonly StatusFlyout _flyout;
    private bool _skipClick;

    public Tray(Action open, Action about, Action quit)
    {
        _open = open;
        _about = about;
        _quit = quit;
        _flyout = new StatusFlyout(open, IconId);
        _flyout.Update(Status(State), TaskbarIsLight());
        _icon = new TrayIcon(IconId, IconPath(State), Tooltip(State));
        _icon.Selected += (_, _) => OnClick();
        _icon.LeftDoubleClick += (_, _) => OnDoubleClick();
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
            Update();
        }
    }

    public void ShowFlyout() => _flyout.Show();

    private void OnClick()
    {
        if (_skipClick)
        {
            _skipClick = false;
            return;
        }

        ShowFlyout();
    }

    // The first click has already opened the flyout, which closes when the window takes focus.
    // The second click arrives as a double-click and then a click, and that click shouldn't reopen the flyout
    private void OnDoubleClick()
    {
        _skipClick = true;
        _open();
    }

    public void Dispose()
    {
        _uiSettings.ColorValuesChanged -= OnColorValuesChanged;
        _icon.Dispose();
        _flyout.Close();
    }

    private void OnColorValuesChanged(UISettings sender, object args) => _dispatcher.TryEnqueue(Update);

    private void Update()
    {
        _flyout.Update(Status(State), TaskbarIsLight());
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
        menu.Items.Add(Item("Show test notification", Notifications.ShowTest));
#if DEBUG
        var states = new MenuFlyoutSubItem { Text = "Icon state" };
        foreach (var state in Enum.GetValues<TrayState>())
        {
            states.Items.Add(Item(state.ToString(), () => State = state));
        }

        menu.Items.Add(states);
#endif
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("About", _about));
        menu.Items.Add(Item("Quit", _quit));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static string IconPath(TrayState state) => IconPath(state, TaskbarIsLight());

    internal static string IconPath(TrayState state, bool light) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Tray", light ? "Light" : "Dark", $"{state}.svg");

    // The taskbar follows the Windows mode, which can differ from the app mode
    private static bool TaskbarIsLight() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    private static string Tooltip(TrayState state) => Program.AppName + "\n" + Status(state);

    private static string Status(TrayState state) => state switch
    {
        TrayState.Idle => "Up to date",
        TrayState.Syncing => "Syncing",
        TrayState.Paused => "Paused",
        TrayState.NeedsAttention => "Needs attention",
        TrayState.Error => "Error",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };
}
