namespace OpenBackupManager.Core.Platform;

// Starts the app when the user signs in
public interface IStartAtLogin
{
    bool IsOn { get; }

    void TurnOn();

    void TurnOff();
}
