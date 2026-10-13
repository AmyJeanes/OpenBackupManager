namespace OpenBackupManager.ViewModels.Tests;

internal sealed class FakeActions : IAppActions
{
    public int Opens { get; private set; }

    public int Restarts { get; private set; }

    public void Open() => Opens++;

    public void About()
    {
    }

    public void Quit()
    {
    }

    public void RestartToUpdate() => Restarts++;
}
