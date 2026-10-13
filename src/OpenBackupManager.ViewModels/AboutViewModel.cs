using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OpenBackupManager.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    // A check that finds nothing can finish very quickly, which can look like the button did nothing
    private static readonly TimeSpan MinimumCheckTime = TimeSpan.FromMilliseconds(500);

    private readonly IUpdater _updater;
    private readonly IAppActions _actions;
    private readonly TimeProvider _time;

    public AboutViewModel(AppInfo app, IUpdater updater, IAppActions actions, TimeProvider time)
    {
        _updater = updater;
        _actions = actions;
        _time = time;
        Name = app.Name;
        Version = "Version " + app.Version + (updater.IsTestSource ? " (test updates)" : "");
        Copyright = app.Copyright;
        UpdateButtonText = ButtonText();
        UpdateStatus = updater.CanUpdate ? Status() : "Updates are unavailable on dev builds";
    }

    public string Name { get; }

    public string Title => "About " + Name;

    public string Version { get; }

    public string? Copyright { get; }

    [ObservableProperty]
    public partial string UpdateButtonText { get; private set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; private set; }

    private bool CanUpdate => _updater.CanUpdate;

    // Restarts to install a waiting update, or checks for one
    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task Update()
    {
        if (_updater.UpdateReady)
        {
            _actions.RestartToUpdate();
            return;
        }

        UpdateStatus = "Checking…";
        // Reported back on this thread
        IProgress<int> downloading = new Progress<int>(percent => UpdateStatus = $"Downloading update… {percent}%");
        var check = _updater.CheckNow(downloading.Report);
        await Task.WhenAll(check, Task.Delay(MinimumCheckTime, _time));
        UpdateStatus = await check switch
        {
            UpdateCheckResult.UpdateReady => ReadyText(),
            UpdateCheckResult.UpToDate => "You're up to date",
            _ => "Couldn't check for updates",
        };
        UpdateButtonText = ButtonText();
    }

    private string ButtonText() => _updater.UpdateReady ? "Restart to update" : "Check for updates";

    private string ReadyText() => $"Version {_updater.ReadyVersion} is ready to install";

    private string Status()
    {
        if (_updater.UpdateReady)
        {
            return ReadyText();
        }

        if (_updater.LastChecked is not { } lastChecked)
        {
            return "Not checked yet";
        }

        var today = _time.GetLocalNow().Date;
        var checkedAt = TimeZoneInfo.ConvertTime(lastChecked, _time.LocalTimeZone);
        var culture = CultureInfo.CurrentCulture;
        var time = checkedAt.ToString("t", culture);
        if (checkedAt.Date == today)
        {
            return $"Last checked at {time}";
        }

        if (checkedAt.Date == today.AddDays(-1))
        {
            return $"Last checked yesterday at {time}";
        }

        return $"Last checked on {checkedAt.ToString("M", culture)} at {time}";
    }
}
