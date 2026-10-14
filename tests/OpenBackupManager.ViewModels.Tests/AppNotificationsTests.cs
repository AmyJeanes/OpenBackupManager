using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.ViewModels.Tests;

[SetCulture("en-GB")]
[SetUICulture("en-GB")]
public class AppNotificationsTests
{
    private FakeNotifier _notifier = null!;
    private FakeActions _actions = null!;
    private AppNotifications _notifications = null!;

    [SetUp]
    public void SetUp()
    {
        _notifier = new FakeNotifier();
        _actions = new FakeActions();
        _notifications = new AppNotifications(_notifier, _actions);
    }

    [TearDown]
    public void TearDown() => _notifications.Dispose();

    [Test]
    public void Test_ViewDetailsOpensTheApp()
    {
        _notifications.ShowTest();
        ClickButton("View details");

        Assert.That(_actions.Opens, Is.EqualTo(1));
    }

    [Test]
    public void UpdateReady_RestartNowRestartsToUpdate()
    {
        _notifications.ShowUpdateReady("1.2.3");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_notifier.Shown.Lines, Is.EqualTo(["Version 1.2.3 is ready to install"]));
            Assert.That(_notifier.Shown.Buttons[1], Is.EqualTo(new DismissButton("Not now")));
        }

        ClickButton("Restart now");
        Assert.That(_actions.Restarts, Is.EqualTo(1));
    }

    [Test]
    public void Updated_LinksToWhatsNew()
    {
        var whatsNew = new Uri("https://example.com/releases/tag/v1.2.3");

        _notifications.ShowUpdated("1.2.3", whatsNew);

        Assert.That(_notifier.Shown.Buttons, Is.EqualTo([new LinkButton("What's new", whatsNew)]));
    }

    [Test]
    public void ConfigError_ClickingItOrShowFileShowsTheFile()
    {
        _notifications.ShowConfigError("Line 3: problem");

        Assert.That(_notifier.Shown.Lines, Is.EqualTo(["Line 3: problem", "Last good settings kept until resolved"]));
        _notifier.Click(_notifier.Shown.Action!);
        ClickButton("Show file");
        Assert.That(_actions.ConfigFileShows, Is.EqualTo(2));
    }

    [Test]
    public void Dispose_StopsHandlingClicks()
    {
        _notifications.ShowTest();
        _notifications.Dispose();

        ClickButton("View details");

        Assert.That(_actions.Opens, Is.Zero);
    }

    private void ClickButton(string label) =>
        _notifier.Click(_notifier.Shown.Buttons.OfType<ActionButton>().Single(b => b.Label == label).Action);

    private sealed class FakeNotifier : INotifier
    {
        public event Action<string>? Clicked;

        public Notification Shown { get; private set; } = null!;

        public void Show(Notification notification) => Shown = notification;

        public void Click(string action) => Clicked?.Invoke(action);
    }
}
