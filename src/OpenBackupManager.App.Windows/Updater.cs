using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace OpenBackupManager.App.Windows;

// Auto updates the application from GitHub Releases
public sealed class Updater : IDisposable
{
    public const string RepositoryUrl = "https://github.com/AmyJeanes/OpenBackupManager";

    public enum CheckResult
    {
        UpToDate,
        UpdateReady,
        Failed,
    }

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager;
    private readonly Action<string> _ready;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private string? _seenVersion;

    // Calls ready when an update has downloaded, and updated when this is the first start of a newer version
    public Updater(string home, Action<string> ready, Action<string> updated)
    {
        _ready = ready;
        // Follows prereleases while running one
        var prerelease = VelopackLocator.Current.CurrentlyInstalledVersion?.IsPrerelease ?? false;
        var source = TestSource();
        IsTestSource = source is not null;
        _manager = source is null
            ? new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease))
            : new UpdateManager(source);
        if (_manager.IsInstalled)
        {
            NoticeUpdate(home, updated);
            _ = CheckRegularly();
        }
    }

    public bool CanUpdate => _manager.IsInstalled;

    public bool UpdateReady => _manager.UpdatePendingRestart is not null;

    public string? ReadyVersion => _manager.UpdatePendingRestart?.Version.ToString();

    // Failed checks don't count
    public DateTime? LastChecked { get; private set; }

    public bool IsTestSource { get; }

    // Reports the download's progress as a percentage. About shows the result, so this doesn't notify
    public Task<CheckResult> CheckNow(Action<int> downloading) => Check(notify: false, downloading);

    // Exits the app
    public void RestartToUpdate() => _manager.ApplyUpdatesAndRestart(_manager.UpdatePendingRestart);

    public void UpdateOnExit()
    {
        if (_manager.UpdatePendingRestart is { } update)
        {
            _manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _checking.Dispose();
    }

    // A folder or URL to check instead of GitHub, for trying updates locally
    private static string? TestSource()
    {
        var source = Environment.GetEnvironmentVariable("OBM_UPDATE_SOURCE");
        if (!string.IsNullOrEmpty(source))
        {
            return source;
        }

        var file = VelopackLocator.Current.RootAppDir is { } root ? Path.Combine(root, "update-source") : null;
        return file is not null && File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    private void NoticeUpdate(string home, Action<string> updated)
    {
        var current = _manager.CurrentVersion!;
        var file = Path.Combine(home, "last-version");
        var last = File.Exists(file) && SemanticVersion.TryParse(File.ReadAllText(file).Trim(), out var version) ? version : null;
        Directory.CreateDirectory(home);
        File.WriteAllText(file, current.ToString());
        if (last is not null && current.CompareTo(last) > 0)
        {
            updated(current.ToString());
        }
    }

    private async Task CheckRegularly()
    {
        using var timer = new PeriodicTimer(CheckInterval);
        try
        {
            do
            {
                await Check(notify: true, downloading: null);
            }
            while (await timer.WaitForNextTickAsync(_stop.Token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<CheckResult> Check(bool notify, Action<int>? downloading)
    {
        // So two downloads never run at once
        await _checking.WaitAsync(_stop.Token);
        try
        {
            var update = await _manager.CheckForUpdatesAsync();
            LastChecked = DateTime.Now;
            if (update is null)
            {
                return CheckResult.UpToDate;
            }

            await _manager.DownloadUpdatesAsync(update, downloading, _stop.Token);
            var version = update.TargetFullRelease.Version.ToString();
            // A version found from About counts as seen, so a later regular check doesn't notify about it
            if (version != _seenVersion)
            {
                _seenVersion = version;
                if (notify)
                {
                    _ready(version);
                }
            }

            return CheckResult.UpdateReady;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            // Offline or GitHub is down. The next check tries again
            return CheckResult.Failed;
        }
        finally
        {
            _checking.Release();
        }
    }
}
