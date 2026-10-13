namespace OpenBackupManager.ViewModels;

// What the tray, notifications, windows and view models ask the app to do
public interface IAppActions
{
    void Open();

    void About();

    void Quit();

    void RestartToUpdate();
}
