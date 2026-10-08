using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Microsoft.Win32;
using NUnit.Framework.Interfaces;

namespace OpenBackupManager.App.Windows.UITests;

// Each test starts the app with a home of its own, so it runs alongside an installed copy that's already open
public abstract class AppTests
{
    protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    // The app's Tray.IconId
    private const uint TrayIconId = 1;

    private static readonly string BuiltAppPath =
        typeof(AppTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "AppPath").Value!;

    private string _home = "";
    private UIA3Automation _automation = null!;
    private Process _process = null!;

    // The flyout the app opens as it starts. Tests wait for it before using the tray, since it takes focus as it opens,
    // which closes the tray menu
    protected Window LaunchFlyout { get; private set; } = null!;

    protected virtual string AppPath => BuiltAppPath;

    protected virtual string AppName => "OpenBackupManager (dev)";

    [SetUp]
    public void Launch()
    {
        _home = Directory.CreateTempSubdirectory("obm-uitests-").FullName;
        _automation = new UIA3Automation();
        _process = LaunchApp();
        LaunchFlyout = WaitForFlyout();
    }

    [TearDown]
    public void Close()
    {
        try
        {
            var failed = TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed;
            if (failed)
            {
                AttachScreenshot();
            }

            // Quitting, rather than ending the process, takes its icon off the taskbar before the next test
            if (!_process.HasExited)
            {
                try
                {
                    Quit();
                }
                catch when (!failed)
                {
                    AttachScreenshot();
                    throw;
                }
            }
        }
        finally
        {
            if (!_process.HasExited)
            {
                _process.Kill();
            }

            _process.Dispose();
            _automation.Dispose();
            Directory.Delete(_home, recursive: true);
        }
    }

    protected Process LaunchApp() => Process.Start(new ProcessStartInfo(AppPath, ["--home", _home]))!;

    // After Quit, as the copy the test goes on with
    protected void LaunchAgain()
    {
        _process.Dispose();
        _process = LaunchApp();
        LaunchFlyout = WaitForFlyout();
    }

    // A copy Windows starts, such as for a notification clicked after Quit, is the one the test goes on with
    protected void WaitForCopyStartedSince(DateTime time)
    {
        var started = Retry.WhileNull(
                () => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppPath)).FirstOrDefault(p => p.StartTime > time),
                Timeout,
                throwOnTimeout: true)
            .Result!;
        _process.Dispose();
        _process = started;
    }

    protected Window WaitForFlyout() => WaitForWindow(IsFlyout);

    protected Window WaitForWindow(string title) => WaitForWindow(w => w.Title == title);

    protected static void WaitUntilHidden(Window window) =>
        Assert.That(Retry.WhileFalse(() => NativeMethods.IsCloaked(window), Timeout).Result, Is.True);

    protected static void CloseWithEsc(Window flyout)
    {
        NativeMethods.BringToFront(flyout);
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        WaitUntilHidden(flyout);
    }

    protected bool IsFlyoutShown() => Windows().Any(w => IsFlyout(w) && !NativeMethods.IsCloaked(w));

    // The middle of the app's tray icon, once it's on the taskbar
    protected Point TrayIcon()
    {
        ShowIconOnTaskbar();
        var taskbar = Taskbar().BoundingRectangle;
        var bounds = Retry.While(
                () => NativeMethods.TrayIconBounds(_process.Id, TrayIconId),
                b => b is not { } icon || !taskbar.Contains(icon),
                Timeout,
                throwOnTimeout: true)
            .Result!.Value;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    protected void ChooseFromTrayMenu(string item)
    {
        Mouse.Click(TrayIcon(), MouseButton.Right);
        // Invoked rather than clicked, since a click that lands while the menu is still opening is lost
        Retry.WhileNull(
                () => Windows().Select(w => w.FindFirstDescendant(c => c.ByControlType(ControlType.MenuItem).And(c.ByName(item)))).FirstOrDefault(i => i is not null),
                Timeout,
                throwOnTimeout: true,
                ignoreException: true)
            .Result!.AsMenuItem().Invoke();
    }

    // Notifications are found in the notification centre rather than as they pop up, as CI has Do Not Disturb enabled
    protected AutomationElement FindNotification(string title, string? text = null)
    {
        var centre = OpenNotificationCentre();
        return Retry.WhileNull(
                () => centre.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))
                    .FirstOrDefault(n => n.FindFirstChild(c => c.ByAutomationId("Title"))?.Name == title
                        && (text is null || n.FindFirstDescendant(c => c.ByName(text)) is not null)),
                Timeout,
                throwOnTimeout: true,
                ignoreException: true)
            .Result!;
    }

    // Windows stacks an app's notifications once there are a few, and a stacked one isn't listed on its own
    protected void ClearNotifications()
    {
        OpenNotificationCentre().FindFirstDescendant(c => c.ByName($"Clear all notifications for {AppName}"))?.AsButton().Invoke();
        CloseNotificationCentre();
    }

    protected AutomationElement OpenNotificationCentre()
    {
        if (NotificationCentre() is null)
        {
            Keyboard.TypeSimultaneously(VirtualKeyShort.LWIN, VirtualKeyShort.KEY_N);
        }

        return Retry.WhileNull(NotificationCentre, Timeout, throwOnTimeout: true, ignoreException: true).Result!;
    }

    protected void CloseNotificationCentre()
    {
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Assert.That(Retry.WhileFalse(() => NotificationCentre() is null, Timeout, ignoreException: true).Result, Is.True);
    }

    protected bool WaitForExit() => _process.WaitForExit(Timeout);

    protected void Quit()
    {
        // Counted once its icon is on the taskbar
        _ = TrayIcon();
        var icons = TrayIconCount();
        ChooseFromTrayMenu("Quit");
        Assert.Multiple(() =>
        {
            Assert.That(WaitForExit(), Is.True);
            Assert.That(Retry.WhileFalse(() => TrayIconCount() < icons, Timeout).Result, Is.True);
        });
    }

    // Saved with the test results, so a failure on CI can be seen
    private static void AttachScreenshot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{TestContext.CurrentContext.Test.Name}.png");
        Capture.Screen().ToFile(path);
        TestContext.AddTestAttachment(path);
    }

    // Windows are cloaked until they're drawn, and the flyout stays open but cloaked while it's hidden
    private Window WaitForWindow(Func<Window, bool> match) =>
        Retry.WhileNull(() => Windows().FirstOrDefault(w => match(w) && !NativeMethods.IsCloaked(w)), Timeout, throwOnTimeout: true, ignoreException: true).Result!;

    private static bool IsFlyout(Window window) => window.FindFirstDescendant(c => c.ByAutomationId("Caption")) is not null;

    // UI Automation can list part of a window, such as where its content takes input, as a window of its own, so each
    // is taken to its window. It can also report every window's process as 0, as it does in Windows Sandbox, so Windows
    // is asked instead. A window can close while they're listed, such as the notification centre after a click, so
    // callers that wait try again
    private IEnumerable<Window> Windows() =>
        _automation.GetDesktop().FindAllChildren()
            .Select(NativeMethods.TopLevelWindow)
            .Distinct()
            .Where(hwnd => NativeMethods.ProcessId(hwnd) == _process.Id)
            .Select(hwnd => _automation.FromHandle(hwnd).AsWindow());

    private AutomationElement? NotificationCentre() =>
        _automation.GetDesktop().FindAllChildren(c => c.ByClassName("Windows.UI.Core.CoreWindow"))
            .Select(w => w.FindFirstChild(c => c.ByAutomationId("NotificationCenterGrid")))
            .FirstOrDefault(c => c is not null);

    private AutomationElement Taskbar() => _automation.GetDesktop().FindFirstChild(c => c.ByClassName("Shell_TrayWnd"))!;

    // Including any left behind by copies that were ended, which stay until the mouse passes over them
    private int TrayIconCount() =>
        Taskbar().FindAllDescendants(c => c.ByAutomationId("NotifyItemIcon")).Count(i => i.Name.StartsWith(AppName, StringComparison.Ordinal));

    // Windows puts a new app's tray icon in the hidden icons, so this moves it onto the taskbar as Settings would.
    // Windows adds the setting for each app's icon when it first sees the icon
    private void ShowIconOnTaskbar()
    {
        var name = Retry.WhileNull(FindIconSettings, Timeout, throwOnTimeout: true).Result!;
        using var settings = Registry.CurrentUser.OpenSubKey($@"Control Panel\NotifyIconSettings\{name}", writable: true)!;
        settings.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
    }

    private string? FindIconSettings()
    {
        using var all = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings");
        return all?.GetSubKeyNames().FirstOrDefault(name =>
        {
            using var settings = all.OpenSubKey(name);
            return string.Equals(settings?.GetValue("ExecutablePath") as string, AppPath, StringComparison.OrdinalIgnoreCase);
        });
    }
}
