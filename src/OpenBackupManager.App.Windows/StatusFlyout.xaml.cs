using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinUIEx;
using CompositionBrush = Windows.UI.Composition.CompositionBrush;
using Compositor = Windows.UI.Composition.Compositor;

namespace OpenBackupManager.App.Windows;

// Created once and cloaked rather than closed, so it opens instantly
public sealed partial class StatusFlyout : Window
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);

    private readonly Action _open;
    private readonly uint _trayIconId;
    private readonly UISettings _uiSettings = new();
    private bool _light;
    private long _hiddenAt;
    private PointInt32 _position;
    private PointInt32 _slideFrom;
    private SizeInt32 _size;
    private long _slideStart;
    private bool _drawn;
    private bool _showWhenDrawn;

    public StatusFlyout(Action open, uint trayIconId)
    {
        InitializeComponent();
        _open = open;
        _trayIconId = trayIconId;
        SystemBackdrop = new BlurBehind();
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(true, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = true;
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        // Starts where it opens, so it's first drawn at that display's scaling
        PlaceNearTray();
        Slide(0);
        AppWindow.Resize(_size);
        AppWindow.Changed += OnAppWindowChanged;
        NativeMethods.Cloak(this.GetWindowHandle(), true);
        AppWindow.Show(false);
        _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateBackground);
        Activated += OnActivated;
        // Opened as the app starts, it waits until it has been drawn
        FirstDraw.WhenDrawn(this, () =>
        {
            _drawn = true;
            if (_showWhenDrawn)
            {
                Show();
            }
        });
    }

    // Follows the Windows mode, like the tray icon, since it opens from the taskbar
    public void Update(string status, bool light)
    {
        StatusText.Text = status;
        _light = light;
        Root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        UpdateBackground();
    }

    public void Show()
    {
        // Clicking the tray icon while the flyout is open should only close it. Pressing the icon gives the taskbar focus,
        // which hides the flyout, and the click then arrives on release, so a click just after hiding is that one
        if (Environment.TickCount64 - _hiddenAt < 300)
        {
            return;
        }

        if (!_drawn)
        {
            _showWhenDrawn = true;
            return;
        }

        PlaceNearTray();
        if (_uiSettings.AnimationsEnabled)
        {
            _slideStart = 0;
            Slide(1);
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            Slide(0);
        }

        AppWindow.Resize(_size);

        Activate();
        this.SetForegroundWindow();
        // Just below the taskbar, so it slides out from behind it rather than over it
        NativeMethods.PlaceBelowTaskbar(this.GetWindowHandle());
        NativeMethods.Cloak(this.GetWindowHandle(), false);
        OpenButton.Focus(FocusState.Programmatic);
    }

    // Windows rescales a window after it moves to a display with different scaling, but the size is already right for it
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidSizeChange && !sender.Size.Equals(_size))
        {
            sender.Resize(_size);
        }
    }

    // Slides in from the taskbar and eases to a stop, like Windows' own tray flyouts
    private void OnRendering(object? sender, object e)
    {
        // Timed from the first frame, which can come a while after the flyout is shown
        if (_slideStart == 0)
        {
            _slideStart = Stopwatch.GetTimestamp();
        }

        var progress = Math.Min(Stopwatch.GetElapsedTime(_slideStart) / SlideDuration, 1);
        Slide(Math.Pow(1 - progress, 3));
        if (progress >= 1)
        {
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void Slide(double remaining) => AppWindow.Move(new PointInt32(
        _position.X + (int)(_slideFrom.X * remaining),
        _position.Y + (int)(_slideFrom.Y * remaining)));

    private void HideFlyout()
    {
        CompositionTarget.Rendering -= OnRendering;
        NativeMethods.Cloak(this.GetWindowHandle(), true);
        _hiddenAt = Environment.TickCount64;
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            HideFlyout();
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        HideFlyout();
        // Esc closes it while it still has focus, which cloaking alone would leave on an invisible window.
        // Hiding hands focus back, and showing it again without activating keeps it drawn for next time
        AppWindow.Hide();
        AppWindow.Show(false);
    }

    private void OnOpen(object sender, RoutedEventArgs e) => _open();

    // One colour laid over the blur, measured to match EarTrumpet's flyout, which reads well on any background.
    // WinUI's acrylic adds a luminosity layer that turns dark backgrounds solid black or light ones too pale
    private void UpdateBackground()
    {
        var opacity = !_uiSettings.AdvancedEffectsEnabled ? 1 : _light ? 0.86 : 0.77;
        var colour = _light ? Color.FromArgb(255, 0xE3, 0xE3, 0xE3) : Color.FromArgb(255, 0x23, 0x23, 0x23);
        Root.Background = new SolidColorBrush(colour) { Opacity = opacity };
    }

    private void PlaceNearTray()
    {
        var icon = NativeMethods.TrayIconBounds(_trayIconId)
            ?? FlyoutPlacement.NotificationAreaCorner(DisplayArea.Primary.OuterBounds, DisplayArea.Primary.WorkArea);
        var display = DisplayArea.GetFromPoint(FlyoutPlacement.Centre(icon), DisplayAreaFallback.Primary);
        // The icon's display, which can have different scaling from the one the flyout was last on
        (_size, _position, _slideFrom) = FlyoutPlacement.Place(display.OuterBounds, display.WorkArea, icon, NativeMethods.Scale(display));
    }

    // Windows' blur of whatever is behind the window
    private sealed partial class BlurBehind : CompositionBrushBackdrop
    {
        protected override CompositionBrush CreateBrush(Compositor compositor) => compositor.CreateHostBackdropBrush();
    }
}
