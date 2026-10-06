using System.Diagnostics;
using System.Reflection;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace OpenBackupManager.App.Windows.UITests;

public sealed class FlyoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private string _home = "";
    private UIA3Automation? _automation;
    private Application? _app;

    [SetUp]
    public void Launch()
    {
        // A home of its own, so it runs alongside any copy that's already open
        _home = Directory.CreateTempSubdirectory("obm-uitests-").FullName;
        _automation = new UIA3Automation();
        _app = Application.Launch(new ProcessStartInfo(AppPath(), ["--home", _home]));
    }

    [TearDown]
    public void Close()
    {
        _app?.Kill();
        _app?.Dispose();
        _automation?.Dispose();
        Directory.Delete(_home, recursive: true);
    }

    [Test]
    public void OpensAtLaunch_AndEscClosesIt()
    {
        var flyout = Retry.WhileNull(FindShownFlyout, Timeout, throwOnTimeout: true).Result!;
        Assert.Multiple(() =>
        {
            Assert.That(flyout.FindFirstDescendant(c => c.ByAutomationId("Caption"))?.Name, Is.EqualTo("OpenBackupManager (dev)"));
            Assert.That(flyout.FindFirstDescendant(c => c.ByAutomationId("StatusText"))?.Name, Is.Not.Empty);
        });

        NativeMethods.BringToFront(flyout);
        Keyboard.Type(VirtualKeyShort.ESCAPE);

        Assert.That(Retry.WhileFalse(() => NativeMethods.IsCloaked(flyout), Timeout).Result, Is.True);
    }

    private static string AppPath() =>
        typeof(FlyoutTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "AppPath").Value!;

    // The flyout stays open but cloaked while it's hidden, so it's only shown once it's uncloaked
    private Window? FindShownFlyout() =>
        _app!.GetAllTopLevelWindows(_automation!)
            .FirstOrDefault(w => w.FindFirstDescendant(c => c.ByAutomationId("Caption")) is not null && !NativeMethods.IsCloaked(w));
}
