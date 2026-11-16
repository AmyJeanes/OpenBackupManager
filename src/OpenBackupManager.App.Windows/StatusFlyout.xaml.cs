using System.Diagnostics;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using OpenBackupManager.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;
using Windows.UI.ViewManagement;
using Windows.Win32;
using WinUIEx;
using WinUIEx.Messaging;
using CompositionBrush = Windows.UI.Composition.CompositionBrush;
using Compositor = Windows.UI.Composition.Compositor;

namespace OpenBackupManager.App.Windows;

// Created once and cloaked rather than closed, so it opens instantly
public sealed partial class StatusFlyout : Window
{
    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(150);

    private readonly uint _trayIconId;
    private readonly UISettings _uiSettings = new();
    private bool _closingByUser;
    private bool _closingForGood;
    private bool _light;
    private long _hiddenAt;
    private PointInt32 _position;
    private PointInt32 _slideFrom;
    private SizeInt32 _size;
    private long _slideStart;
    private bool _sliding;
    private bool _drawn;
    private bool _showWhenDrawn;
    private bool _shown;

    public StatusFlyout(StatusViewModel viewModel, uint trayIconId)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Title = viewModel.AppName;
        // Never seen on the flyout, but Task Manager lists its window under the app with it
        AppWindow.SetIcon(AppIcons.Ico);
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
        // The reason under the status can come or go while it's open, as just after the app starts
        Detail.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Height != e.PreviousSize.Height)
            {
                Refit();
            }
        };
        NativeMethods.Cloak(this.GetWindowHandle(), true);
        AppWindow.Show(false);
        var ui = SynchronizationContext.Current!;
        _uiSettings.AdvancedEffectsEnabledChanged += (_, _) => ui.Post(_ => UpdateBackground(), null);
        Activated += OnActivated;
        AppWindow.Closing += OnClosing;
        var messages = new WindowMessageMonitor(this);
        messages.WindowMessageReceived += OnMessage;
        Closed += (_, _) => messages.Dispose();
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

    public StatusViewModel ViewModel { get; }

    // Asked to close, with whether by the user, with Alt+F4 or the window's menu, rather than by another program
    public event Action<bool>? CloseRequested;

    // Follows the Windows mode, like the tray icon, since it opens from the taskbar
    public void Update(bool light)
    {
        _light = light;
        Root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        UpdateBackground();
    }

    public bool JustHidden => Environment.TickCount64 - _hiddenAt < 300;

    // As the app quits
    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    public void Show()
    {
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
            _sliding = true;
            CompositionTarget.Rendering += OnRendering;
        }
        else
        {
            Slide(0);
        }

        AppWindow.Resize(_size);

        _shown = true;
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
            _sliding = false;
            CompositionTarget.Rendering -= OnRendering;
        }
    }

    private void Slide(double remaining) => AppWindow.Move(new PointInt32(
        _position.X + (int)(_slideFrom.X * remaining),
        _position.Y + (int)(_slideFrom.Y * remaining)));

    private void HideFlyout()
    {
        _shown = false;
        _sliding = false;
        CompositionTarget.Rendering -= OnRendering;
        NativeMethods.Cloak(this.GetWindowHandle(), true);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        // Only a press elsewhere, such as on the tray icon, while it's shown; Esc and Alt+F4 have hidden it already
        if (args.WindowActivationState == WindowActivationState.Deactivated && _shown)
        {
            HideFlyout();
            _hiddenAt = Environment.TickCount64;
        }
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => Dismiss();

    // While it still has focus, which cloaking alone would leave on an invisible window. Hiding hands focus back, and
    // showing it again without activating keeps it drawn for next time
    private void Dismiss()
    {
        HideFlyout();
        AppWindow.Hide();
        AppWindow.Show(false);
    }

    // Alt+F4 and the window's menu ask with this before closing it, where another program, as taskkill does, only closes it
    private void OnMessage(object? sender, WindowMessageEventArgs e)
    {
        if (e.Message.MessageId == PInvoke.WM_SYSCOMMAND && (e.Message.WParam & 0xFFF0) == PInvoke.SC_CLOSE)
        {
            _closingByUser = true;
        }
    }

    // Kept, as the tray opens it again, and SessionEnd hears Windows signing out through its window
    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closingForGood)
        {
            return;
        }

        args.Cancel = true;
        var byUser = _closingByUser;
        _closingByUser = false;
        Dismiss();
        CloseRequested?.Invoke(byUser);
    }

    // One colour laid over the blur, measured to match EarTrumpet's flyout, which reads well on any background.
    // WinUI's acrylic adds a luminosity layer that turns dark backgrounds solid black or light ones too pale
    private void UpdateBackground()
    {
        var opacity = !_uiSettings.AdvancedEffectsEnabled ? 1 : _light ? 0.86 : 0.77;
        var colour = _light ? Color.FromArgb(255, 0xE3, 0xE3, 0xE3) : Color.FromArgb(255, 0x23, 0x23, 0x23);
        Root.Background = new SolidColorBrush(colour) { Opacity = opacity };
    }

    // A slide picks up the new position in its next frame
    private void Refit()
    {
        PlaceNearTray();
        AppWindow.Resize(_size);
        if (!_sliding)
        {
            Slide(0);
        }
    }

    private void PlaceNearTray()
    {
        var icon = NativeMethods.TrayIconBounds(_trayIconId)
            ?? FlyoutPlacement.NotificationAreaCorner(DisplayArea.Primary.OuterBounds, DisplayArea.Primary.WorkArea);
        var display = DisplayArea.GetFromPoint(FlyoutPlacement.Centre(icon), DisplayAreaFallback.Primary);
        // The icon's display, which can have different scaling from the one the flyout was last on
        (_size, _position, _slideFrom) = FlyoutPlacement.Place(display.OuterBounds, display.WorkArea, icon, NativeMethods.Scale(display), ContentHeight());
    }

    // As tall as the status needs, such as when it says why the engine can't be reached, plus the window's frame
    private double ContentHeight()
    {
        if (Root.XamlRoot is not { } root)
        {
            return FlyoutPlacement.MinHeight;
        }

        Root.Measure(new Size(Root.ActualWidth, double.PositiveInfinity));
        return Root.DesiredSize.Height + ((AppWindow.Size.Height - AppWindow.ClientSize.Height) / root.RasterizationScale);
    }

    // Windows' blur of whatever is behind the window
    private sealed partial class BlurBehind : CompositionBrushBackdrop
    {
        protected override CompositionBrush CreateBrush(Compositor compositor) => compositor.CreateHostBackdropBrush();
    }
}
