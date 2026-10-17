using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.ViewModels.Tests;

[SetUICulture("en-GB")]
public class StatusViewModelTests
{
    private static readonly AppInfo App = new("Open Backup Manager", "1.2.3", null, new Uri("https://example.com/obm"));

    [Test]
    public void StartsIdle_WhileTheEngineStarts()
    {
        var status = new StatusViewModel(App, new FakeActions());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.AppName, Is.EqualTo("Open Backup Manager"));
            Assert.That(status.State, Is.EqualTo(SyncState.Idle));
            Assert.That(status.Engine, Is.EqualTo(EngineState.Starting));
            Assert.That(status.StatusText, Is.EqualTo("Starting"));
        }
    }

    [TestCase(SyncState.Idle, "Up to date")]
    [TestCase(SyncState.Syncing, "Syncing")]
    [TestCase(SyncState.Paused, "Paused")]
    [TestCase(SyncState.NeedsAttention, "Needs attention")]
    [TestCase(SyncState.Error, "Error")]
    public void StatusText_DescribesTheState(SyncState state, string text) =>
        Assert.That(new StatusViewModel(App, new FakeActions()) { Engine = EngineState.Connected, State = state }.StatusText, Is.EqualTo(text));

    [TestCase(EngineState.Starting, "Starting")]
    [TestCase(EngineState.Unreachable, "Can't reach the engine")]
    public void StatusText_UntilTheEngineIsConnected_DescribesThat(EngineState engine, string text) =>
        Assert.That(new StatusViewModel(App, new FakeActions()) { Engine = engine, State = SyncState.Syncing }.StatusText, Is.EqualTo(text));

    [Test]
    public void ChangingTheEngine_ChangesTheText()
    {
        var status = new StatusViewModel(App, new FakeActions());
        var changed = new List<string?>();
        status.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        status.Engine = EngineState.Connected;

        Assert.That(changed, Is.EquivalentTo([nameof(StatusViewModel.Engine), nameof(StatusViewModel.StatusText), nameof(StatusViewModel.Detail)]));
    }

    [TestCase(EngineProblem.AnotherUserOrAdmin, "Another user or an administrator started the engine for this home, so the app can't use it.")]
    [TestCase(EngineProblem.AnotherVersion, "Another version of the engine is still running. Signing out and back in restarts it.")]
    [TestCase(EngineProblem.Other, "")]
    [TestCase(null, "")]
    public void Detail_SaysWhyTheEngineCantBeReached(EngineProblem? problem, string detail) =>
        Assert.That(new StatusViewModel(App, new FakeActions()) { Engine = EngineState.Unreachable, Problem = problem }.Detail, Is.EqualTo(detail));

    [TestCase(EngineState.Starting)]
    [TestCase(EngineState.Connected)]
    public void Detail_IsEmpty_UnlessTheEngineCantBeReached(EngineState engine) =>
        Assert.That(new StatusViewModel(App, new FakeActions()) { Engine = engine, Problem = EngineProblem.AnotherVersion }.Detail, Is.Empty);

    [Test]
    public void ChangingTheProblem_ChangesTheDetail()
    {
        var status = new StatusViewModel(App, new FakeActions());
        var changed = new List<string?>();
        status.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        status.Problem = EngineProblem.AnotherVersion;

        Assert.That(changed, Is.EquivalentTo([nameof(StatusViewModel.Problem), nameof(StatusViewModel.Detail)]));
    }

    [Test]
    public void ChangingTheState_ChangesTheText()
    {
        var status = new StatusViewModel(App, new FakeActions());
        var changed = new List<string?>();
        status.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        status.State = SyncState.Syncing;

        Assert.That(changed, Is.EquivalentTo([nameof(StatusViewModel.State), nameof(StatusViewModel.StatusText)]));
    }

    [Test]
    public void Open_OpensTheMainWindow()
    {
        var actions = new FakeActions();

        new StatusViewModel(App, actions).OpenCommand.Execute(null);

        Assert.That(actions.Opens, Is.EqualTo(1));
    }
}
