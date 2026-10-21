using System.Diagnostics;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Tools;
using Microsoft.Win32;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

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

    // Holds only obm, which installing puts on PATH
    private static readonly string CommandLineFolder = Path.Combine(InstallFolder, "current", "bin");

    private string _releases = "";
    private string _feed = "";

    protected override string AppPath => Path.Combine(InstallFolder, "current", "OpenBackupManager.App.Windows.exe");

    protected override string AppName => "Open Backup Manager";

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
        Environment.SetEnvironmentVariable("OBM_HOME", null);
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
        // Only the installed app's own home starts at login without asking, and the test's home is another
        File.WriteAllText(Path.Combine(Home, "config.toml"), "version = 1\nstart-at-login = true\n");
        // Velopack runs its hooks without --home, so they stop the engine for this home, as the app's copies inherit it
        Environment.SetEnvironmentVariable("OBM_HOME", Home);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UserPathEntries(), Does.Contain(CommandLineFolder));
            Assert.That(Directory.GetFiles(CommandLineFolder).Select(Path.GetFileName), Is.EqualTo((string[])["obm.exe"]));
            Assert.That(Obm("--version"), Does.StartWith("0.0.1+"));
        }

        var engine = EngineOf("0.0.1");
        ChooseFromTrayMenu("About");
        var about = WaitForWindow("About " + AppName);
        Assert.That(Text(about, "VersionText"), Is.EqualTo("Version 0.0.1 (test updates)"));
        // Checks wait for each other, so once this one finishes, the check the app made as it started has too, and
        // only About's next check can find 0.0.2
        CheckForUpdates(about, "You're up to date");
        // As when offline
        var away = _feed + "-away";
        Directory.Move(_feed, away);
        CheckForUpdates(about, "Couldn't check for updates");
        Directory.Move(away, _feed);
        AddToFeed("0.0.2");
        CheckForUpdates(about, "Version 0.0.2 is ready to install");
        var stops = EngineStops();
        var restarted = DateTime.Now;
        var restart = UpdateButton(about, "Restart to update");
        try
        {
            restart.Invoke();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ElementNotAvailableException)
        {
            // UI Automation reports a failure when the app quits before the invoke returns, as this one does
        }

        WaitForRestartAfterUpdate(restarted);
        CheckEngineStoppedFirst(engine, stops, "0.0.2");
        Assert.That(Obm("status"), Does.Contain("Version: 0.0.2+"));

        FindNotification("Update installed", "Version 0.0.2 is now installed");
        ClearNotifications();
        Quit();

        AddToFeed("0.0.3");
        LaunchAgain();
        FindNotification("Update available", "Version 0.0.3 is ready to install");
        ClearNotifications();
        engine = EngineOf("0.0.2");
        stops = EngineStops();
        Quit();
        Assert.That(Retry.WhileTrue(IsUpdaterRunning, Timeout).Result, Is.True, "The update should install after Quit");

        LaunchAgain();
        CheckEngineStoppedFirst(engine, stops, "0.0.3");
        FindNotification("Update installed", "Version 0.0.3 is now installed");
        ClearNotifications();
        Quit();

        AddToFeed("0.0.4");
        // Turned off, it doesn't check as it starts, and turned back on, it checks straight away
        var config = Path.Combine(Home, "config.toml");
        File.WriteAllText(config, "version = 1\nstart-at-login = true\n\n[updates]\nautomatic = false\n");
        LaunchAgain();
        engine = EngineOf("0.0.3");
        ChooseFromTrayMenu("About");
        about = WaitForWindow("About " + AppName);
        Assert.That(Text(about, "UpdateStatusText"), Is.EqualTo("Automatic update checks are off"));
        File.WriteAllText(config, "version = 1\nstart-at-login = true\n");
        FindNotification("Update available", "Version 0.0.4 is ready to install");
        ClearNotifications();
        // Its engine keeps running, until Velopack's hook stops it as the next start installs the update
        EndApp();
        stops = EngineStops();
        var started = DateTime.Now;
        StartAgain();
        WaitForRestartAfterUpdate(started);
        CheckEngineStoppedFirst(engine, stops, "0.0.4");
        FindNotification("Update installed", "Version 0.0.4 is now installed");
        CloseNotificationCentre();
        Quit();

        var registrations = NotificationRegistrations().ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registrations, Is.Not.Empty);
            Assert.That(RunEntries(), Does.Contain($"\"{AppPath}\" --home \"{Home}\" --at-login"));
        }

        using var byHand = StartEngineByHand();
        Assert.That(Retry.WhileFalse(() => EngineStatus()?.ProcessId == byHand.Id, Timeout, ignoreException: true).Result, Is.True);
        Uninstall();
        Assert.Multiple(() =>
        {
            Assert.That(byHand.HasExited, Is.True, "Uninstalling should end the engine");
            Assert.That(UserPathEntries(), Does.Not.Contain(CommandLineFolder), "obm should be taken off PATH");
            Assert.That(Retry.WhileTrue(() => Directory.Exists(InstallFolder), Timeout).Result, Is.True, "The install folder should be removed");
            Assert.That(KeyExists(UninstallKey), Is.False);
            Assert.That(registrations.Where(KeyExists), Is.Empty, "The notification registration should be removed");
            Assert.That(RunEntries(), Is.Empty, "Starting at login should be removed");
            Assert.That(
                Retry.WhileFalse(() => !OpenNotificationCentre().FindAllDescendants(c => c.ByControlType(ControlType.ListItem))
                    .Any(n => n.FindFirstChild(c => c.ByAutomationId("Title"))?.Name == "Update installed"), Timeout, ignoreException: true).Result,
                Is.True,
                "The app's notifications should be cleared");
        });
        CloseNotificationCentre();
    }

    // The engine the app started, once it has, as the given version
    private StatusResult EngineOf(string version)
    {
        StatusResult? status = null;
        Assert.That(
            Retry.WhileFalse(() => (status = EngineStatus())?.Version.StartsWith(version + "+", StringComparison.Ordinal) is true, Timeout, ignoreException: true).Result,
            Is.True,
            $"The engine should be running {version}, not {status?.Version}");
        return status!;
    }

    // Installing ends whatever runs from the install folder, so the engine should stop first, which only a clean stop
    // logs, and the app should start the new version's once it's back
    private void CheckEngineStoppedFirst(StatusResult engine, int stopsBefore, string version)
    {
        var next = EngineOf(version);
        using (Assert.EnterMultipleScope())
        {
            // Not by process ID, which Windows can give the new engine straight after the old one ends
            Assert.That(next.Started, Is.GreaterThan(engine.Started), "The app should have started a new engine");
            Assert.That(EngineStops(), Is.EqualTo(stopsBefore + 1), $"The {engine.Version} engine should stop before {version} installs");
        }
    }

    private int EngineStops() =>
        Directory.GetFiles(AppPaths.InHome(Home).Logs, "engine-*.log").Sum(log =>
        {
            // Shared, as a running engine has it open
            using var reader = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            return reader.ReadToEnd().Split('\n').Count(line => line.Contains("Engine stopped", StringComparison.Ordinal));
        });

    // The installed obm, as a terminal would start it from PATH
    private static string Obm(string arguments)
    {
        using var obm = Process.Start(new ProcessStartInfo(Path.Combine(CommandLineFolder, "obm.exe"), arguments) { RedirectStandardOutput = true })!;
        var output = obm.StandardOutput.ReadToEnd();
        obm.WaitForExit();
        return output;
    }

    private static string[] UserPathEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment")!;
        return (key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").Split(';');
    }

    // Puts a version in the feed the installed app checks
    private void AddToFeed(string version)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(_releases, version)))
        {
            File.Copy(file, Path.Combine(_feed, Path.GetFileName(file)), overwrite: true);
        }
    }

    // The updater runs the app for a moment for each of its hooks, then starts it again
    private void WaitForRestartAfterUpdate(DateTime since)
    {
        Assert.That(WaitForExit() && Retry.WhileTrue(IsUpdaterRunning, Timeout).Result, Is.True, "The update should install");
        WaitForCopyStartedSince(since);
        _ = WaitForFlyout();
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
                // An updater that exits while it's being read has no main module, or throws
                return p.MainModule?.FileName.StartsWith(InstallFolder, StringComparison.OrdinalIgnoreCase) is true;
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

    // The commands in the user's Run key that start the installed app
    private List<string> RunEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return key?.GetValueNames()
            .Select(name => key.GetValue(name) as string)
            .OfType<string>()
            .Where(command => command.StartsWith($"\"{AppPath}\"", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
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
