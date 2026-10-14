using System.Globalization;
using Microsoft.Extensions.Time.Testing;

namespace OpenBackupManager.ViewModels.Tests;

[SetCulture("en-GB")]
[SetUICulture("en-GB")]
public class AboutViewModelTests
{
    private static readonly AppInfo App = new("Open Backup Manager", "1.2.3", "© 2026 Amy Jeanes", new Uri("https://example.com/obm"));

    private FakeTimeProvider _time = null!;
    private FakeUpdater _updater = null!;
    private FakeActions _actions = null!;
    private SynchronizationContext? _previousContext;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero));
        _updater = new FakeUpdater();
        _actions = new FakeActions();
        _previousContext = SynchronizationContext.Current;
        // Progress reports and the command's continuations run straight away, as they would on an idle UI thread
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());
    }

    [TearDown]
    public void RestoreContext() => SynchronizationContext.SetSynchronizationContext(_previousContext);

    [Test]
    public void ShowsTheAppsDetails()
    {
        var about = About();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.Title, Is.EqualTo("About Open Backup Manager"));
            Assert.That(about.Name, Is.EqualTo("Open Backup Manager"));
            Assert.That(about.Version, Is.EqualTo("Version 1.2.3"));
            Assert.That(about.Copyright, Is.EqualTo("© 2026 Amy Jeanes"));
        }
    }

    [Test]
    public void Links_AreInTheRepository()
    {
        var about = About();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.GitHubLink, Is.EqualTo(new Uri("https://example.com/obm")));
            Assert.That(about.LicenceLink, Is.EqualTo(new Uri("https://example.com/obm/blob/main/LICENSE")));
            Assert.That(about.ThirdPartyNoticesLink, Is.EqualTo(new Uri("https://example.com/obm/blob/main/THIRD-PARTY-NOTICES.md")));
        }
    }

    [Test]
    public void Version_SaysWhenUpdatesComeFromATestSource()
    {
        _updater.IsTestSource = true;

        Assert.That(About().Version, Is.EqualTo("Version 1.2.3 (test updates)"));
    }

    [Test]
    public void DevBuilds_CantUpdate()
    {
        _updater.CanUpdate = false;
        var about = About();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.UpdateStatus, Is.EqualTo("Updates are unavailable on dev builds"));
            Assert.That(about.UpdateCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void Status_BeforeTheFirstCheck() => Assert.That(About().UpdateStatus, Is.EqualTo("Not checked yet"));

    [TestCase("2026-10-13T09:30:00Z", "Last checked at 09:30")]
    [TestCase("2026-10-12T23:10:00Z", "Last checked yesterday at 23:10")]
    [TestCase("2026-10-01T08:00:00Z", "Last checked on 1 October at 08:00")]
    public void Status_SaysWhenItLastChecked(string lastChecked, string status)
    {
        _updater.LastChecked = DateTimeOffset.Parse(lastChecked, CultureInfo.InvariantCulture);

        Assert.That(About().UpdateStatus, Is.EqualTo(status));
    }

    [Test]
    public void Status_UsesLocalTime()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2"));
        _updater.LastChecked = new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.Zero);

        Assert.That(About().UpdateStatus, Is.EqualTo("Last checked at 01:30"));
    }

    [Test]
    public void ReadyUpdate_OffersARestart()
    {
        _updater.ReadyVersion = "1.2.4";
        var about = About();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.UpdateStatus, Is.EqualTo("Version 1.2.4 is ready to install"));
            Assert.That(about.UpdateButtonText, Is.EqualTo("Restart to update"));
        }
    }

    [Test]
    public void Restart_InstallsTheReadyUpdate()
    {
        _updater.ReadyVersion = "1.2.4";

        About().UpdateCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_actions.Restarts, Is.EqualTo(1));
            Assert.That(_updater.Checks, Is.Zero);
        }
    }

    [Test]
    public void Check_ShowsProgressAndTheResult()
    {
        var about = About();
        Assert.That(about.UpdateButtonText, Is.EqualTo("Check for updates"));

        var run = about.UpdateCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.UpdateStatus, Is.EqualTo("Checking…"));
            Assert.That(about.UpdateCommand.CanExecute(null), Is.False);
        }

        _updater.Downloading(42);
        Assert.That(about.UpdateStatus, Is.EqualTo("Downloading update… 42%"));

        _updater.ReadyVersion = "1.2.4";
        _updater.Finish(UpdateCheckResult.UpdateReady);
        _time.Advance(TimeSpan.FromMilliseconds(500));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.IsCompletedSuccessfully, Is.True);
            Assert.That(about.UpdateStatus, Is.EqualTo("Version 1.2.4 is ready to install"));
            Assert.That(about.UpdateButtonText, Is.EqualTo("Restart to update"));
            Assert.That(about.UpdateCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public void Check_ShowsCheckingForAtLeastHalfASecond()
    {
        var about = About();

        var run = about.UpdateCommand.ExecuteAsync(null);
        _updater.Finish(UpdateCheckResult.UpToDate);
        _time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.That(about.UpdateStatus, Is.EqualTo("Checking…"));

        _time.Advance(TimeSpan.FromMilliseconds(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(run.IsCompletedSuccessfully, Is.True);
            Assert.That(about.UpdateStatus, Is.EqualTo("You're up to date"));
        }
    }

    [Test]
    public void Check_SaysWhenItFailed()
    {
        var about = About();

        about.UpdateCommand.Execute(null);
        _updater.Finish(UpdateCheckResult.Failed);
        _time.Advance(TimeSpan.FromMilliseconds(500));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(about.UpdateStatus, Is.EqualTo("Couldn't check for updates"));
            Assert.That(about.UpdateButtonText, Is.EqualTo("Check for updates"));
        }
    }

    private AboutViewModel About() => new(App, _updater, _actions, _time);

    private sealed class FakeUpdater : IUpdater
    {
        private TaskCompletionSource<UpdateCheckResult> _check = new();
        private Action<int> _downloading = _ => { };

        public bool CanUpdate { get; set; } = true;

        public bool IsTestSource { get; set; }

        public bool UpdateReady => ReadyVersion is not null;

        public string? ReadyVersion { get; set; }

        public DateTimeOffset? LastChecked { get; set; }

        public int Checks { get; private set; }

        public Task<UpdateCheckResult> CheckNow(Action<int> downloading)
        {
            Checks++;
            _downloading = downloading;
            _check = new TaskCompletionSource<UpdateCheckResult>();
            return _check.Task;
        }

        public void Downloading(int percent) => _downloading(percent);

        public void Finish(UpdateCheckResult result) => _check.SetResult(result);
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }
}
