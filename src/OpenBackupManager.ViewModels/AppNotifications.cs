using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.ViewModels;

// What the app's notifications say, and what clicking them does
public sealed class AppNotifications : IDisposable
{
    private const string ViewDetails = "viewDetails";
    private const string Restart = "restart";
    private const string ShowConfig = "showConfig";

    private readonly INotifier _notifier;
    private readonly IAppActions _actions;

    public AppNotifications(INotifier notifier, IAppActions actions)
    {
        _notifier = notifier;
        _actions = actions;
        notifier.Clicked += OnClicked;
    }

    public void Dispose() => _notifier.Clicked -= OnClicked;

    public void ShowTest() => _notifier.Show(new Notification(Strings.Notification_TestTitle, [Strings.Notification_TestText], ViewDetails)
    {
        Buttons = [new ActionButton(Strings.Notification_ViewDetails, ViewDetails)],
    });

    public void ShowUpdateReady(string version) => _notifier.Show(new Notification(
        Strings.Notification_UpdateReadyTitle,
        [Text.Format(Strings.Notification_UpdateReadyText, version)])
    {
        Buttons = [new ActionButton(Strings.Notification_RestartNow, Restart), new DismissButton(Strings.Notification_NotNow)],
    });

    public void ShowUpdated(string version, Uri whatsNew) => _notifier.Show(new Notification(
        Strings.Notification_UpdatedTitle,
        [Text.Format(Strings.Notification_UpdatedText, version)])
    {
        Buttons = [new LinkButton(Strings.Notification_WhatsNew, whatsNew)],
    });

    public void ShowConfigError(string problem) => _notifier.Show(new Notification(
        Strings.Notification_ConfigErrorTitle,
        [problem, Strings.Notification_ConfigErrorKept],
        ShowConfig)
    {
        Buttons = [new ActionButton(Strings.Notification_ShowFile, ShowConfig)],
    });

    private void OnClicked(string action)
    {
        switch (action)
        {
            case ViewDetails:
                _actions.Open();
                break;
            case Restart:
                _actions.RestartToUpdate();
                break;
            case ShowConfig:
                _actions.ShowConfigFile();
                break;
        }
    }
}
