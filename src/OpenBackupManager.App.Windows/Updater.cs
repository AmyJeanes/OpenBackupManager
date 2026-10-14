using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.ViewModels;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace OpenBackupManager.App.Windows;

// Auto updates the application from GitHub Releases
public sealed partial class Updater : IUpdater, IDisposable
{
    public const string RepositoryUrl = "https://github.com/AmyJeanes/OpenBackupManager";

    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly AppPaths _paths;
    private readonly ConfigWatcher _config;
    private readonly AppNotifications _notifications;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _checking = new(1, 1);
    private readonly Lock _lock = new();
    private volatile UpdateManager _manager;
    private bool _prereleases;
    private string? _seenVersion;
    private bool _disposed;

    public Updater(AppPaths paths, ConfigWatcher config, AppNotifications notifications, ILogger<Updater> logger)
    {
        _paths = paths;
        _config = config;
        _notifications = notifications;
        _logger = logger;
        var source = TestSource();
        IsTestSource = source is not null;
        if (source is null)
        {
            _prereleases = Prereleases(config.Config);
            _manager = FromGitHub(_prereleases);
            config.Changed += OnConfigChanged;
        }
        else
        {
            _manager = new UpdateManager(source);
        }

        if (_manager.IsInstalled)
        {
            NoticeUpdate();
            _ = CheckRegularly();
        }
    }

    public bool CanUpdate => _manager.IsInstalled;

    public bool UpdateReady => _manager.UpdatePendingRestart is not null;

    public string? ReadyVersion => _manager.UpdatePendingRestart?.Version.ToString();

    public DateTimeOffset? LastChecked { get; private set; }

    public bool IsTestSource { get; }

    // About shows the result, so this doesn't notify
    public Task<UpdateCheckResult> CheckNow(Action<int> downloading) => Check(notify: false, downloading);

    // Exits the app. Velopack restarts it without its arguments, so the home is passed on
    public void RestartToUpdate() => _manager.ApplyUpdatesAndRestart(_manager.UpdatePendingRestart, ["--home", Program.Home]);

    public void UpdateOnExit()
    {
        if (_manager.UpdatePendingRestart is { } update)
        {
            LogInstallingOnExit(update.Version);
            _manager.WaitExitThenApplyUpdates(update, silent: true, restart: false);
        }
    }

    // Called twice on Quit, since the services dispose it as both Updater and IUpdater
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _config.Changed -= OnConfigChanged;
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

    // Left out, updates follow what's installed, so a prerelease gets prereleases
    private static bool Prereleases(AppConfig config) =>
        config.Updates.Prereleases ?? VelopackLocator.Current.CurrentlyInstalledVersion?.IsPrerelease ?? false;

    private static UpdateManager FromGitHub(bool prereleases) => new(new GithubSource(RepositoryUrl, null, prereleases));

    private void OnConfigChanged(ConfigResult _)
    {
        // Changes can be raised from two threads at once
        lock (_lock)
        {
            var prereleases = Prereleases(_config.Config);
            if (prereleases == _prereleases)
            {
                return;
            }

            _prereleases = prereleases;
            // Takes effect from the next check. One already running finishes with the manager it started with
            _manager = FromGitHub(prereleases);
            LogPrereleasesChanged(prereleases);
        }
    }

    private void NoticeUpdate()
    {
        var current = _manager.CurrentVersion!;
        var file = Path.Combine(_paths.State, "last-version");
        var last = File.Exists(file) && SemanticVersion.TryParse(File.ReadAllText(file).Trim(), out var version) ? version : null;
        Directory.CreateDirectory(_paths.State);
        File.WriteAllText(file, current.ToString());
        if (last is not null && current.CompareTo(last) > 0)
        {
            LogUpdated(last, current);
            _notifications.ShowUpdated(current.ToString(), new Uri($"{RepositoryUrl}/releases/tag/v{current}"));
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

    private async Task<UpdateCheckResult> Check(bool notify, Action<int>? downloading)
    {
        // So two downloads never run at once
        await _checking.WaitAsync(_stop.Token);
        var manager = _manager;
        try
        {
            var update = await manager.CheckForUpdatesAsync();
            LastChecked = DateTimeOffset.Now;
            if (update is null)
            {
                LogUpToDate();
                return UpdateCheckResult.UpToDate;
            }

            await manager.DownloadUpdatesAsync(update, downloading, _stop.Token);
            var version = update.TargetFullRelease.Version.ToString();
            LogDownloaded(version);
            // A version found from About counts as seen, so a later regular check doesn't notify about it
            if (version != _seenVersion)
            {
                _seenVersion = version;
                if (notify)
                {
                    _notifications.ShowUpdateReady(version);
                }
            }

            return UpdateCheckResult.UpdateReady;
        }
        catch (Exception e) when (e is HttpRequestException or IOException)
        {
            LogCheckFailed(e);
            // Offline or GitHub is down. The next check tries again
            return UpdateCheckResult.Failed;
        }
        finally
        {
            _checking.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated from version {Last} to {Current}")]
    private partial void LogUpdated(SemanticVersion last, SemanticVersion current);

    [LoggerMessage(Level = LogLevel.Information, Message = "Up to date")]
    private partial void LogUpToDate();

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded version {Version}")]
    private partial void LogDownloaded(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't check for updates")]
    private partial void LogCheckFailed(Exception e);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updates now include prereleases: {Prereleases}")]
    private partial void LogPrereleasesChanged(bool prereleases);

    [LoggerMessage(Level = LogLevel.Information, Message = "Installing version {Version} once the app exits")]
    private partial void LogInstallingOnExit(SemanticVersion version);
}
