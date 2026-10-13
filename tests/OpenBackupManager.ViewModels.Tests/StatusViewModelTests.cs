namespace OpenBackupManager.ViewModels.Tests;

public class StatusViewModelTests
{
    private static readonly AppInfo App = new("OpenBackupManager", "1.2.3", null);

    [Test]
    public void StartsIdle()
    {
        var status = new StatusViewModel(App, new FakeActions());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(status.AppName, Is.EqualTo("OpenBackupManager"));
            Assert.That(status.State, Is.EqualTo(SyncState.Idle));
            Assert.That(status.StatusText, Is.EqualTo("Up to date"));
        }
    }

    [TestCase(SyncState.Idle, "Up to date")]
    [TestCase(SyncState.Syncing, "Syncing")]
    [TestCase(SyncState.Paused, "Paused")]
    [TestCase(SyncState.NeedsAttention, "Needs attention")]
    [TestCase(SyncState.Error, "Error")]
    public void StatusText_DescribesTheState(SyncState state, string text) =>
        Assert.That(new StatusViewModel(App, new FakeActions()) { State = state }.StatusText, Is.EqualTo(text));

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
