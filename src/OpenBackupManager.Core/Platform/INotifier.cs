namespace OpenBackupManager.Core.Platform;

// Shows desktop notifications through the OS
public interface INotifier
{
    // With the action of the notification or button that was clicked
    event Action<string>? Clicked;

    void Show(Notification notification);
}
