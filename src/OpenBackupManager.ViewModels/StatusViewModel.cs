using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.ViewModels;

// The status the tray icon and the status flyout show
public sealed partial class StatusViewModel(AppInfo app, IAppActions actions) : ObservableObject
{
    private readonly IAppActions _actions = actions;

    public string AppName { get; } = app.Name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial SyncState State { get; set; }

    // Until it's connected, that's what the status says rather than how syncing is going
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(Detail))]
    public partial EngineState Engine { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail))]
    public partial EngineProblem? Problem { get; set; }

    public string StatusText => Engine switch
    {
        EngineState.Starting => Strings.Status_EngineStarting,
        EngineState.Unreachable => Strings.Status_EngineUnreachable,
        _ => SyncText,
    };

    // Under the status, for the problems someone can do something about
    public string Detail => Engine != EngineState.Unreachable ? "" : Problem switch
    {
        EngineProblem.AnotherUserOrAdmin => Strings.Status_EngineAnotherUserOrAdmin,
        EngineProblem.AnotherVersion => Strings.Status_EngineAnotherVersion,
        _ => "",
    };

    private string SyncText => State switch
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
