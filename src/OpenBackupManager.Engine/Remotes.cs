using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.Rclone;

namespace OpenBackupManager.Engine;

// An rclone rcd for each remote in config.toml, as each account has its own sign-in, throttling and limits, and a
// crash only takes its own folders
internal sealed partial class Remotes(ConfigWatcher config, RcloneConfig rcloneConfig, AppPaths paths, TimeProvider time, ILogger<Remotes> logger)
    : IHostedService, IDisposable
{
    // Doubling between tries, so one that keeps failing doesn't spin, and back to the first once one has stayed up
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

    // Not doubling, so one starts soon after it's unlocked, and a check costs little
    private static readonly TimeSpan LockedCheck = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stopping = new();
    private readonly Lock _lock = new();
    private List<Remote> _remotes = [];

    // Taken out of config.toml, until their rcd has quit
    private readonly List<Remote> _leaving = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _remotes = [.. config.Config.Remotes.Select(remote => Start(remote.Id))];
            config.Changed += Follow;
        }

        return Task.CompletedTask;
    }

    // All at once, as each takes 1.5 s to quit
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        config.Changed -= Follow;
        await _stopping.CancelAsync();
        await Running;
    }

    public void Dispose()
    {
        foreach (var remote in _remotes.Concat(_leaving))
        {
            remote.Stopping.Dispose();
        }

        _stopping.Dispose();
    }

    // Ends once every remote has stopped trying
    internal Task Running
    {
        get
        {
            lock (_lock)
            {
                return Task.WhenAll(_remotes.Concat(_leaving).Select(remote => remote.Running));
            }
        }
    }

    internal IReadOnlyList<RemoteStatus> Status()
    {
        lock (_lock)
        {
            return [.. _remotes.Select(remote => remote.Status)];
        }
    }

    // Starts one again that's stopped trying, such as once its password is back, or tries now one that's waiting to
    internal void Retry(string id)
    {
        lock (_lock)
        {
            var remote = _remotes.Find(remote => remote.Id == id) ?? throw new EngineException($"There's no remote called {id}");
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            if (remote.Status.State is RemoteState.NeedsSetup or RemoteState.Stopped)
            {
                remote.Status = new RemoteStatus(id, RemoteState.Starting);
                remote.Running = Task.Run(() => RunAsync(remote), CancellationToken.None);
            }
            else if (remote.Status.State is RemoteState.Retrying or RemoteState.WaitingForSecrets)
            {
                // Not Cancel, which would carry on its run on this thread, holding the lock
                _ = remote.TryNow?.CancelAsync();
            }
        }
    }

    // Starts those added to config.toml and stops those taken out. An edit with an error leaves Config as it was
    private void Follow(ConfigResult result)
    {
        lock (_lock)
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            var ids = config.Config.Remotes.Select(remote => remote.Id).ToHashSet();
            foreach (var gone in _remotes.Where(remote => !ids.Contains(remote.Id)))
            {
                LogTakenOut(gone.Id);
                _leaving.Add(gone);
                _ = LeaveAsync(gone);
            }

            _remotes = [.. config.Config.Remotes.Select(settings => _remotes.Find(remote => remote.Id == settings.Id) ?? Start(settings.Id))];
        }
    }

    private Remote Start(string id)
    {
        var remote = new Remote(id, _stopping.Token);
        remote.Running = Task.Run(() => RunAsync(remote), CancellationToken.None);
        return remote;
    }

    private async Task LeaveAsync(Remote remote)
    {
        // Not Cancel, which would quit its rcd on this thread, holding the lock
        await remote.Stopping.CancelAsync();
        await remote.Running;
        lock (_lock)
        {
            _leaving.Remove(remote);
        }

        remote.Stopping.Dispose();
    }

    // Its files are named from its ID, which config.toml only allows to be letters, digits and hyphens. The socket's is
    // a hash, as the run folder limits how long its names can be. Setup's has its own socket and a copy of the config,
    // so the remote's own rcd can start beside it
    internal RcdOptions Options(string id, bool setup = false)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..8];
        return new RcdOptions(
            BundledRclone.FilePath,
            RunFolder.Socket(paths, $"{(setup ? "su" : "rc")}-{hash}.sock"),
            rcloneConfig.FileFor(id) + (setup ? ".setup" : ""),
            Path.Combine(paths.Cache, "rclone", id),
            Path.Combine(paths.Logs, $"rclone-{id}.log"),
            UserAgent);
    }

    internal static string UserAgent => $"ISV|AmyJeanes|OpenBackupManager/{AppVersion.Current}";

    // Until the engine stops, or it needs setting up
    private async Task RunAsync(Remote remote)
    {
        var stopping = remote.Stopping.Token;
        var retry = FirstRetry;
        var waiting = false;
        try
        {
            while (true)
            {
                var started = time.GetTimestamp();
                // Not between checks of a locked store, which status would only show for a moment
                if (!waiting)
                {
                    Set(remote, new RemoteStatus(remote.Id, RemoteState.Starting));
                }

                var (rcd, problem) = await StartAsync(remote, stopping);
                if (problem?.Reason == RemoteReason.SecretStoreLocked)
                {
                    var check = new RemoteStatus(remote.Id, RemoteState.WaitingForSecrets, problem.Reason, problem.Value, time.GetUtcNow() + LockedCheck);
                    if (!waiting)
                    {
                        Set(remote, check);
                        LogWaitingForSecrets(remote.Id, check.InEnglish!);
                        waiting = true;
                    }

                    await WaitToRetryAsync(remote, check, LockedCheck, stopping);
                    continue;
                }

                if (waiting)
                {
                    LogSecretsUnlocked(remote.Id);
                    waiting = false;
                }

                if (rcd is not null)
                {
                    await using (rcd)
                    {
                        Set(remote, new RemoteStatus(remote.Id, RemoteState.Running));
                        LogStarted(remote.Id);
                        await rcd.Exited.WaitAsync(stopping);
                        if (time.GetElapsedTime(started) >= LongestRetry)
                        {
                            retry = FirstRetry;
                        }

                        LogExited(remote.Id, rcd.ExitCode, retry.TotalSeconds);
                        problem = new Problem(RemoteReason.Exited, rcd.ExitCode.ToString(CultureInfo.InvariantCulture));
                    }
                }

                if (problem is null)
                {
                    return;
                }

                var status = new RemoteStatus(remote.Id, RemoteState.Retrying, problem.Reason, problem.Value, time.GetUtcNow() + retry);
                await WaitToRetryAsync(remote, status, retry, stopping);
                retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
        }
        // A bug, logged now rather than thrown when the engine stops
        catch (Exception e)
        {
            Set(remote, new RemoteStatus(remote.Id, RemoteState.Stopped, RemoteReason.Bug, e.Message));
            LogStoppedRunning(e, remote.Id);
        }
    }

    // Until it's time, or it's asked to try now
    private async Task WaitToRetryAsync(Remote remote, RemoteStatus status, TimeSpan retry, CancellationToken stopping)
    {
        using var tryNow = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        lock (_lock)
        {
            remote.Status = status;
            remote.TryNow = tryNow;
        }

        try
        {
            await Task.Delay(retry, time, tryNow.Token);
        }
        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
        {
        }
        finally
        {
            lock (_lock)
            {
                remote.TryNow = null;
            }
        }
    }

    // With no rcd and no problem when it needs setting up, which isn't tried again. Its file is left, as putting its
    // password back brings it back
    private async Task<(Rcd? Rcd, Problem? Problem)> StartAsync(Remote remote, CancellationToken stopping)
    {
        var options = Options(remote.Id);
        if (!File.Exists(options.Config))
        {
            NeedsSettingUp(remote, RemoteReason.NoConfig);
            return (null, null);
        }

        try
        {
            Secret? password = null;
            if (RcloneConfigFile.IsEncrypted(options.Config))
            {
                Secret? secret;
                try
                {
                    secret = await rcloneConfig.ReadPasswordAsync(stopping);
                }
                // Only a chosen store can be read, and so locked
                catch (SecretStoreLockedException)
                {
                    return (null, new Problem(RemoteReason.SecretStoreLocked, rcloneConfig.Store!));
                }
                // Such as Secret Service with no D-Bus session
                catch (InvalidOperationException e)
                {
                    LogCantStart(e, remote.Id);
                    return (null, new Problem(RemoteReason.CouldntStart, e.Message));
                }

                if (secret is null)
                {
                    NeedsSettingUp(remote, RemoteReason.NoPassword);
                    return (null, null);
                }

                password = secret;
            }

            RunFolder.Create(paths);
            PrivateFiles.CreateFolder(Path.GetDirectoryName(options.LogFile)!);

            var rcd = await Rcd.StartAsync(options, stopping);
            // rclone reads its config lazily, so a wrong password or a damaged file only shows here
            try
            {
                // Rather than in its environment, which anything it starts would get too
                if (password is not null)
                {
                    await rcd.Client.UnlockConfigAsync(password.Reveal(), stopping);
                }

                await rcd.Client.ListRemotesAsync(stopping);
            }
            // Not rclone's message, which can quote a line of the config
            catch (RcloneException)
            {
                await rcd.DisposeAsync();
                NeedsSettingUp(remote, password is null ? RemoteReason.ConfigUnreadable : RemoteReason.WrongPassword, options.LogFile);
                return (null, null);
            }
            catch
            {
                await rcd.DisposeAsync();
                throw;
            }

            return (rcd, null);
        }
        catch (Exception e) when (e is RcloneException or HttpRequestException or IOException or UnauthorizedAccessException
            or Win32Exception or EngineException or InvalidDataException)
        {
            LogCantStart(e, remote.Id);
            return (null, new Problem(RemoteReason.CouldntStart, e.Message));
        }
    }

    private void NeedsSettingUp(Remote remote, RemoteReason reason, string? value = null)
    {
        var status = new RemoteStatus(remote.Id, RemoteState.NeedsSetup, reason, value);
        Set(remote, status);
        LogNeedsSettingUp(remote.Id, status.InEnglish!);
    }

    // Before it's logged, so whatever waits for the log sees it
    private void Set(Remote remote, RemoteStatus status)
    {
        lock (_lock)
        {
            remote.Status = status;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Started rclone for remote {Id}")]
    private partial void LogStarted(string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Couldn't start rclone for remote {Id}")]
    private partial void LogCantStart(Exception e, string id);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stopped running rclone for remote {Id}")]
    private partial void LogStoppedRunning(Exception e, string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote {Id} needs setting up, as {Reason}")]
    private partial void LogNeedsSettingUp(string id, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopping rclone for remote {Id}, as it's been taken out of config.toml")]
    private partial void LogTakenOut(string id);

    [LoggerMessage(Level = LogLevel.Warning, Message = "rclone for remote {Id} stopped with exit code {ExitCode}, restarting in {Seconds} s")]
    private partial void LogExited(string id, int exitCode, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Remote {Id} is waiting, as {Reason}. It carries on once it's unlocked, which it never asks for")]
    private partial void LogWaitingForSecrets(string id, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "The secret store is unlocked, so remote {Id} carries on")]
    private partial void LogSecretsUnlocked(string id);

    // Why it's waiting to try again
    private sealed record Problem(RemoteReason Reason, string Value);

    private sealed class Remote(string id, CancellationToken engineStopping)
    {
        public string Id { get; } = id;

        // Once the engine stops, or it's taken out of config.toml
        public CancellationTokenSource Stopping { get; } = CancellationTokenSource.CreateLinkedTokenSource(engineStopping);

        public RemoteStatus Status { get; set; } = new(id, RemoteState.Starting);

        public Task Running { get; set; } = Task.CompletedTask;

        // While it's waiting to try again
        public CancellationTokenSource? TryNow { get; set; }
    }
}
