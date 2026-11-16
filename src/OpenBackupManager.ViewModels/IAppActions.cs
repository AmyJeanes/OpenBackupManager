namespace OpenBackupManager.ViewModels;

// What the tray, notifications, windows and view models ask the app to do
public interface IAppActions
{
    void Open();

    void About();

    // Quits once the user says to, unless ask-before-quitting is off
    void AskToQuit();

    void Quit();

    void RestartToUpdate();

    void ShowConfigFile();

    void ShowLicence();

    void ShowThirdPartyNotices();
}
