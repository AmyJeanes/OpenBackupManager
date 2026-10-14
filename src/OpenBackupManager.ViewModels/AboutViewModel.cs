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
        Version = Text.Format(updater.IsTestSource ? Strings.About_VersionTestUpdates : Strings.About_Version, app.Version);
        Copyright = app.Copyright;
        GitHubLink = app.Repository;
        LicenceLink = new Uri($"{app.Repository}/blob/main/LICENSE");
        ThirdPartyNoticesLink = new Uri($"{app.Repository}/blob/main/THIRD-PARTY-NOTICES.md");
        UpdateButtonText = ButtonText();
        UpdateStatus = updater.CanUpdate ? Status() : Strings.About_UpdatesUnavailable;
    }

    public string Name { get; }

    public string Title => Text.Format(Strings.About_Title, Name);

    public string Version { get; }

    public string? Copyright { get; }

    public Uri GitHubLink { get; }

    public Uri LicenceLink { get; }

    public Uri ThirdPartyNoticesLink { get; }

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

        UpdateStatus = Strings.About_Checking;
        // Reported back on this thread
        IProgress<int> downloading = new Progress<int>(percent => UpdateStatus = Text.Format(Strings.About_Downloading, percent));
        var check = _updater.CheckNow(downloading.Report);
        await Task.WhenAll(check, Task.Delay(MinimumCheckTime, _time));
        UpdateStatus = await check switch
        {
            UpdateCheckResult.UpdateReady => ReadyText(),
            UpdateCheckResult.UpToDate => Strings.About_UpToDate,
            _ => Strings.About_CheckFailed,
        };
        UpdateButtonText = ButtonText();
    }

    private string ButtonText() => _updater.UpdateReady ? Strings.About_RestartToUpdate : Strings.About_CheckForUpdates;

    private string ReadyText() => Text.Format(Strings.About_ReadyToInstall, _updater.ReadyVersion);

    private string Status()
    {
        if (_updater.UpdateReady)
        {
            return ReadyText();
        }

        if (_updater.LastChecked is not { } lastChecked)
        {
            return Strings.About_NotChecked;
        }

        var today = _time.GetLocalNow().Date;
        var checkedAt = TimeZoneInfo.ConvertTime(lastChecked, _time.LocalTimeZone);
        var culture = CultureInfo.CurrentCulture;
        var time = checkedAt.ToString("t", culture);
        if (checkedAt.Date == today)
        {
            return Text.Format(Strings.About_LastCheckedToday, time);
        }

        if (checkedAt.Date == today.AddDays(-1))
        {
            return Text.Format(Strings.About_LastCheckedYesterday, time);
        }

        return Text.Format(Strings.About_LastCheckedOn, checkedAt.ToString("M", culture), time);
    }
}
