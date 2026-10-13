using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenBackupManager.ViewModels;

// The status the tray icon and the status flyout show
public sealed partial class StatusViewModel(AppInfo app, IAppActions actions) : ObservableObject
{
    private readonly IAppActions _actions = actions;

    public string AppName { get; } = app.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial SyncState State { get; set; }

    public string StatusText => State switch
    {
        SyncState.Idle => Strings.Status_Idle,
        SyncState.Syncing => Strings.Status_Syncing,
        SyncState.Paused => Strings.Status_Paused,
        SyncState.NeedsAttention => Strings.Status_NeedsAttention,
        SyncState.Error => Strings.Status_Error,
        _ => throw new ArgumentOutOfRangeException(nameof(State)),
    };

    [RelayCommand]
    private void Open() => _actions.Open();
}
