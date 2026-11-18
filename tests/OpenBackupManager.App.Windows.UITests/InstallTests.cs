using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Tools;
using Microsoft.Win32;
using NUnit.Framework.Interfaces;
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
    private static readonly string CommandLineFolder = Path.Combine(InstallFolder, "bin");

    // Where the installed app checks for updates, read as it starts
    private static readonly string UpdateSource = Path.Combine(InstallFolder, "update-source");

    private string _releases = "";
    private string _feed = "";
    private List<string> _registrations = [];

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
        File.WriteAllText(UpdateSource, _feed);
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
            // Install may have failed before making it
            if (Directory.Exists(_feed))
            {
                Directory.Delete(_feed, recursive: true);
            }
        }
    }

    // What installing and uninstalling left, so a failure on CI can be followed: Velopack's logs, which of the app's
    // notification registrations remain, and Windows' own store of notifications
    [TearDown]
    public void AttachWindowsState()
    {
        if (TestContext.CurrentContext.Result.Outcome.Status != TestStatus.Failed)
        {
            return;
        }

        var folder = Directory.CreateTempSubdirectory("obm-install-state-").FullName;
        var options = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1, IgnoreInaccessible = true };
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Path.GetTempPath() })
        {
            foreach (var log in Directory.EnumerateFiles(root, "velopack*.log", options))
            {
                CopyOpen(log, Path.Combine(folder, Path.GetFileName(Path.GetDirectoryName(log)) + "-" + Path.GetFileName(log)));
            }
        }

        File.WriteAllLines(
            Path.Combine(folder, "registrations.txt"),
            _registrations.Select(key => $"{(KeyExists(key) ? "still there" : "removed")}: {key}"));
        var store = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Notifications");
        foreach (var file in new[] { "wpndatabase.db", "wpndatabase.db-wal", "wpndatabase.db-shm" })
        {
            CopyOpen(Path.Combine(store, file), Path.Combine(folder, file));
        }

        var path = Path.Combine(Path.GetTempPath(), $"{TestContext.CurrentContext.Test.Name}-windows.zip");
        File.Delete(path);
        ZipFile.CreateFromDirectory(folder, path);
        Directory.Delete(folder, recursive: true);
        TestContext.AddTestAttachment(path);
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
            Assert.That(Directory.GetFiles(CommandLineFolder).Select(Path.GetFileName), Is.EquivalentTo((string[])["obm.exe", "sq.version"]));
            Assert.That(Obm("--version"), Does.StartWith("0.0.1+"));
            Assert.That(new FileInfo(Path.Combine(InstallFolder, "current", "LICENSE.txt")).Length, Is.GreaterThan(0));
            // The generated part, not only the hand-written notices the app's build copies
            Assert.That(File.ReadAllText(Path.Combine(InstallFolder, "current", "THIRD-PARTY-NOTICES.txt")), Does.Contain("Microsoft.WindowsAppSDK.WinUI"));
        }

        // As VS Code has every folder on PATH open, through every update
        using var onPath = new OpenFolders(UserPathEntries().Where(entry => entry.StartsWith(InstallFolder, StringComparison.OrdinalIgnoreCase)));
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
        try
        {
            CheckForUpdates(about, "Couldn't check for updates");
        }
        finally
        {
            Directory.Move(away, _feed);
        }

        // As a web filter's page in place of the package, which fails Velopack's checksum
        AddToFeed("0.0.2", broken: true);
        CheckForUpdates(about, "Couldn't check for updates");
        AddToFeed("0.0.2");
        CheckForUpdates(about, "Version 0.0.2 is ready to install");
        AutomationElement notInstalled;
        // As a File Explorer window open in the install folder has it, which Velopack can't install past
        using (new OpenFolders([Path.Combine(InstallFolder, "current", "Assets")]))
        {
            var tried = DateTime.Now;
            RestartToUpdate(about);
            WaitForRestartAfterUpdate(tried);
            notInstalled = FindNotification("Update not installed", "Version 0.0.2 couldn't be installed");
        }

        engine = EngineOf("0.0.1");
        var stops = EngineStops();
        var restarted = DateTime.Now;
        PressNotificationButton(notInstalled, "Try again");
        WaitForRestartAfterUpdate(restarted);
        CheckEngineStoppedFirst(engine, stops, "0.0.2");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Obm("--version"), Does.StartWith("0.0.2+"));
            Assert.That(Obm("status"), Does.Contain("Version: 0.0.2+"));
        }

        CheckObmFromATerminal();
        CheckDiagnosticsHaveVelopacksLog();

        FindNotification("Update installed", "Version 0.0.2 is now installed");
        ClearNotifications();
        Quit();

        AddToFeed("0.0.3");
        LaunchAgain();
        FindNotification("Update available", "Version 0.0.3 is ready to install");
        ClearNotifications();
        // A newer version's download that fails leaves the update that's waiting
        AddToFeed("0.0.4", broken: true);
        var config = Path.Combine(Home, "config.toml");
        WaitForLog("Checks for updates automatically: false", () => File.WriteAllText(config, "version = 1\nstart-at-login = true\n\n[updates]\nautomatic = false\n"));
        // Turned back on, it checks straight away
        WaitForLog("Couldn't check for updates", () => File.WriteAllText(config, "version = 1\nstart-at-login = true\n"));
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
        File.WriteAllText(config, "version = 1\nstart-at-login = true\n\n[updates]\nautomatic = false\n");
        // Quitting part way through About's download
        using (var held = new HeldFeed(_feed))
        {
            File.WriteAllText(UpdateSource, held.Url);
            LaunchAgain();
            ChooseFromTrayMenu("About");
            about = WaitForWindow("About " + AppName);
            UpdateButton(about, "Check for updates").Invoke();
            Assert.That(
                Retry.WhileFalse(() => Text(about, "UpdateStatusText")?.StartsWith("Downloading update", StringComparison.Ordinal) is true, Timeout).Result,
                Is.True,
                "It should start downloading");
            Quit();
        }

        File.WriteAllText(UpdateSource, _feed);
        Assert.That(Directory.GetFiles(Home, "crash-*.txt", SearchOption.AllDirectories), Is.Empty, "Quitting shouldn't crash");
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
        _registrations = registrations;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(registrations, Is.Not.Empty);
            Assert.That(RunEntries(), Does.Contain($"\"{AppPath}\" --home \"{Home}\" --at-login"));
        }

        // Velopack can't remove a folder that's open
        onPath.Dispose();
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

    private static void RestartToUpdate(Window about)
    {
        try
        {
            UpdateButton(about, "Restart to update").Invoke();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ElementNotAvailableException)
        {
            // UI Automation reports a failure when the app quits before the invoke returns, as this one does
        }
    }

    // Buttons only show once the notification is expanded
    private static void PressNotificationButton(AutomationElement notification, string name)
    {
        notification.FindFirstChild(c => c.ByAutomationId("ExpandButton"))?.AsButton().Invoke();
        var button = Retry.WhileNull(
                () => notification.FindFirstChild(c => c.ByControlType(ControlType.Button).And(c.ByName(name))),
                Timeout,
                throwOnTimeout: true,
                ignoreException: true)
            .Result!.AsButton();
        try
        {
            button.Invoke();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or ElementNotAvailableException)
        {
            // The app quits to install, which can be before the invoke returns
        }
    }

    // The installed obm, as a terminal would start it from PATH
    private static string Obm(string arguments)
    {
        using var obm = Process.Start(new ProcessStartInfo(Path.Combine(CommandLineFolder, "obm.exe"), arguments) { RedirectStandardOutput = true })!;
        var output = obm.StandardOutput.ReadToEnd();
        obm.WaitForExit();
        return output;
    }

    // Velopack writes the user's folder in its log with each backslash twice, as well as once
    private static void CheckDiagnosticsHaveVelopacksLog()
    {
        var folder = Directory.CreateTempSubdirectory("obm-diagnostics-").FullName;
        try
        {
            var zip = Path.Combine(folder, "diagnostics.zip");
            Obm($"diagnostics export -o \"{zip}\"");
            using var archive = ZipFile.OpenRead(zip);
            var entry = archive.GetEntry("logs/velopack_OpenBackupManager.App.log");
            Assert.That(entry, Is.Not.Null, string.Join(", ", archive.Entries.Select(file => file.FullName)));
            using var reader = new StreamReader(entry!.Open());
            var log = reader.ReadToEnd();
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(log, Does.Contain("Package version 0.0.2 applied successfully"));
                Assert.That(log, Does.Not.Contain(home).IgnoreCase);
                Assert.That(log, Does.Not.Contain(home.Replace(@"\", @"\\", StringComparison.Ordinal)).IgnoreCase);
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // From a terminal, with no OBM_HOME, obm should use the installed app's home, and find obmd to remove its secrets
    // with. With no input, it lists what it would remove and stops at the question, so nothing is removed
    private static void CheckObmFromATerminal()
    {
        var home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenBackupManager");
        // Only a folder that's there is listed
        var made = !Directory.Exists(home);
        Directory.CreateDirectory(home);
        try
        {
            var start = new ProcessStartInfo(Path.Combine(CommandLineFolder, "obm.exe"), "data remove") { RedirectStandardOutput = true, RedirectStandardInput = true };
            start.Environment.Remove("OBM_HOME");
            using var obm = Process.Start(start)!;
            var output = obm.StandardOutput.ReadToEnd();
            obm.WaitForExit();
            Assert.That(output.Split('\n').Select(line => line.Trim()), Does.Contain(home), output);
        }
        finally
        {
            if (made)
            {
                Directory.Delete(home, recursive: true);
            }
        }
    }

    private static string[] UserPathEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment")!;
        return (key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "").Split(';');
    }

    // Puts a version in the feed the installed app checks. Broken, its packages are cut short
    private void AddToFeed(string version, bool broken = false)
    {
        foreach (var file in Directory.GetFiles(Path.Combine(_releases, version)))
        {
            var target = Path.Combine(_feed, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
            if (broken && Path.GetExtension(file) == ".nupkg")
            {
                using var package = File.OpenWrite(target);
                package.SetLength(package.Length / 2);
            }
        }
    }

    // Once the app has logged the text again
    private void WaitForLog(string text, Action action)
    {
        var before = Logged(text);
        action();
        Assert.That(Retry.WhileFalse(() => Logged(text) > before, Timeout).Result, Is.True, $"The app should log \"{text}\"");
    }

    private int Logged(string text) => Directory.GetFiles(Path.Combine(Home, "logs"), "app-*.log").Sum(file =>
    {
        using var reader = new StreamReader(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        return Regex.Count(reader.ReadToEnd(), Regex.Escape(text));
    });

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

    // Copied while whatever has it open keeps writing, as Windows does its notification store. Skipped if it's not there
    private static void CopyOpen(string from, string to)
    {
        try
        {
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = File.Create(to);
            source.CopyTo(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.WriteAllText(to + ".error.txt", e.Message);
        }
    }

    // Has each folder open as programs watching it do, which keeps anything from renaming the folders it's in
    private sealed class OpenFolders(IEnumerable<string> folders) : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = [.. folders.Select(folder => new FileSystemWatcher(folder) { EnableRaisingEvents = true })];

        public void Dispose() => _watchers.ForEach(watcher => watcher.Dispose());
    }

    private static bool KeyExists(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key is not null;
    }
}
