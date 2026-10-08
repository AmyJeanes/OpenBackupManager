using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Microsoft.Win32;

namespace OpenBackupManager.App.Windows.UITests;

// Installs, updates and uninstalls the app as a user would. It replaces the installed app, so it only runs when asked
// for, as CI does. OBM_TEST_RELEASES is a folder of feeds packed by tools/Publish-TestUpdate.ps1, one for each version,
// each with that version and those before it
[Explicit("Replaces the installed app")]
[Category("Install")]
public sealed class InstallTests : AppTests
{
    private static readonly string InstallFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenBackupManager.App");

    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);

    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\OpenBackupManager.App";

    private string _releases = "";
    private string _feed = "";

    protected override string AppPath => Path.Combine(InstallFolder, "current", "OpenBackupManager.App.Windows.exe");

    protected override string AppName => "OpenBackupManager";

    // Before the app starts
    [OneTimeSetUp]
    public void Install()
    {
        _releases = Environment.GetEnvironmentVariable("OBM_TEST_RELEASES") ?? "";
        Assert.That(Directory.Exists(_releases), Is.True, "OBM_TEST_RELEASES should be a folder of feeds");
        _feed = Directory.CreateTempSubdirectory("obm-feed-").FullName;
        AddToFeed("0.0.1");
        Run(Directory.GetFiles(_feed, "*-Setup.exe").Single(), "--silent");
        File.WriteAllText(Path.Combine(InstallFolder, "update-source"), _feed);
    }

    [OneTimeTearDown]
    public void CleanUp()
    {
        try
        {
            if (KeyExists(UninstallKey))
            {
                Uninstall();
            }
        }
        finally
        {
            Directory.Delete(_feed, recursive: true);
        }
    }

    [Test]
    public void UpdatesAndUninstalls()
    {
        ChooseFromTrayMenu("About");
        var about = WaitForWindow("About " + AppName);
        Assert.That(Text(about, "VersionText"), Is.EqualTo("Version 0.0.1 (test updates)"));
        // Checks wait for each other, so once this one finishes, the check the app made as it started has too, and
        // only About's next check can find 0.0.2
        CheckForUpdates(about, "You're up to date");
        AddToFeed("0.0.2");
        CheckForUpdates(about, "Version 0.0.2 is ready to install");
        var restarted = DateTime.Now;
        var restart = UpdateButton(about, "Restart to update");
        try
        {
            restart.Invoke();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // UI Automation reports a failure when the app quits before the invoke returns, as this one does
        }

        // The updater runs the app for a moment for each of its hooks before it restarts it
        Assert.That(WaitForExit() && Retry.WhileTrue(IsUpdaterRunning, Timeout).Result, Is.True, "The update should install");
        WaitForCopyStartedSince(restarted);
        _ = WaitForFlyout();

        FindNotification("Update installed", "Version 0.0.2 is now installed");
        ClearNotifications();
        Quit();

        AddToFeed("0.0.3");
        LaunchAgain();
        FindNotification("Update available", "Version 0.0.3 is ready to install");
        ClearNotifications();
        Quit();
        Assert.That(Retry.WhileTrue(IsUpdaterRunning, Timeout).Result, Is.True, "The update should install after Quit");

        LaunchAgain();
        FindNotification("Update installed", "Version 0.0.3 is now installed");
        CloseNotificationCentre();
        Quit();

        var registrations = NotificationRegistrations().ToList();
        Assert.That(registrations, Is.Not.Empty);
        Uninstall();
        Assert.Multiple(() =>
        {
            Assert.That(Retry.WhileTrue(() => Directory.Exists(InstallFolder), Timeout).Result, Is.True, "The install folder should be removed");
            Assert.That(KeyExists(UninstallKey), Is.False);
            Assert.That(registrations.Where(KeyExists), Is.Empty, "The notification registration should be removed");
            Assert.That(
                Retry.WhileFalse(() => !OpenNotificationCentre().FindAllDescendants(c => c.ByControlType(ControlType.ListItem))
                    .Any(n => n.FindFirstChild(c => c.ByAutomationId("Title"))?.Name == "Update installed"), Timeout, ignoreException: true).Result,
                Is.True,
                "The app's notifications should be cleared");
        });
        CloseNotificationCentre();
    }

    // Puts a version in the feed the installed app checks
    private void AddToFeed(string version)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(_releases, version)))
        {
            File.Copy(file, Path.Combine(_feed, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void CheckForUpdates(Window about, string result)
    {
        UpdateButton(about, "Check for updates").Invoke();
        Assert.That(Retry.While(() => Text(about, "UpdateStatusText"), status => status != result, Timeout).Result, Is.EqualTo(result));
    }

    private static string? Text(Window window, string id) => window.FindFirstDescendant(c => c.ByAutomationId(id))?.Name;

    private static Button UpdateButton(Window about, string name)
    {
        var button = about.FindFirstDescendant(c => c.ByAutomationId("UpdateButton"))!.AsButton();
        Assert.That(button.Name, Is.EqualTo(name));
        return button;
    }

    // Velopack's updater, which installs an update once the app has quit
    private static bool IsUpdaterRunning() =>
        Process.GetProcessesByName("Update").Any(p =>
        {
            try
            {
                return p.MainModule!.FileName.StartsWith(InstallFolder, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                return false;
            }
        });

    // The keys the app registers for its notifications: its ID, found by its icon, and the COM class Windows calls for clicks
    private static IEnumerable<string> NotificationRegistrations()
    {
        using var ids = Registry.CurrentUser.OpenSubKey(@"Software\Classes\AppUserModelId")!;
        foreach (var name in ids.GetSubKeyNames())
        {
            using var id = ids.OpenSubKey(name)!;
            if (id.GetValue("IconUri") is string icon && icon.StartsWith(InstallFolder, StringComparison.OrdinalIgnoreCase))
            {
                yield return $@"Software\Classes\AppUserModelId\{name}";
                yield return $@"Software\Classes\CLSID\{id.GetValue("CustomActivator")}";
            }
        }
    }

    // With the command Velopack registers for uninstalling without asking, as IT tools do
    private static void Uninstall()
    {
        string command;
        using (var key = Registry.CurrentUser.OpenSubKey(UninstallKey)!)
        {
            command = (string)key.GetValue("QuietUninstallString")!;
        }

        var exeEnd = command.IndexOf('"', 1);
        Run(command[1..exeEnd], command[(exeEnd + 1)..]);
    }

    private static void Run(string path, string arguments)
    {
        using var process = Process.Start(path, arguments)!;
        if (!process.WaitForExit(InstallTimeout))
        {
            Assert.Fail($"{Path.GetFileName(path)} should finish");
        }

        Assert.That(process.ExitCode, Is.Zero, $"{Path.GetFileName(path)} should succeed");
    }

    private static bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }
}
