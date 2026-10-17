using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

// Keeps the app connected to the engine for its home, starting obmd from beside the app when none is running, and again
// whenever it goes. The engine keeps running if the app crashes, and the next copy connects to it
public sealed partial class EngineConnection(string home, AppPaths paths, StatusViewModel status, TimeProvider time, ILogger<EngineConnection> logger) : IDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromMinutes(1);

    // Connected for this long counts as working again, so the next retry is quick
    private static readonly TimeSpan Steady = TimeSpan.FromMinutes(1);

    private readonly string _home = home;
    private readonly AppPaths _paths = paths;
    private readonly StatusViewModel _status = status;
    private readonly TimeProvider _time = time;
    private readonly ILogger _logger = logger;
    private readonly SynchronizationContext _ui = SynchronizationContext.Current!;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _running;
    private volatile EngineClient? _engine;

    public void Start() => _running = Task.Run(() => KeepConnectedAsync(_stopping.Token));

    // Stops trying to connect, and stops the engine if the app started it, as Quit does
    public async Task QuitAsync()
    {
        await StopConnectingAsync();
        if (_engine is not { } engine)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            var status = await engine.StatusAsync(timeout.Token);
            if (!status.StartedByApp)
            {
                LogLeftRunning();
                return;
            }

            // Until it exits it holds its lock, so a copy started straight after would find it still running
            using var process = Process.GetProcessById(status.ProcessId);
            await engine.StopAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            LogStopped();
        }
        catch (OperationCanceledException)
        {
            LogDidntStop(StopTimeout);
        }
        catch (Exception e) when (e is EngineException or ArgumentException)
        {
            LogCouldntStop(e);
        }
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _engine?.Dispose();
        _stopping.Dispose();
    }

    private async Task StopConnectingAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await (_running ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task KeepConnectedAsync(CancellationToken cancellationToken)
    {
        var retry = FirstRetry;
        while (true)
        {
            EngineClient? engine = null;
            EngineProblem? problem = null;
            try
            {
                engine = await ConnectOrStartAsync(cancellationToken);
            }
            catch (EngineException e)
            {
                LogCantReach(e);
                problem = e.Problem;
            }

            if (engine is not null)
            {
                var connected = _time.GetUtcNow();
                _engine = engine;
                Show(EngineState.Connected);
                LogConnected();

                // Left open on cancelling, for QuitAsync to use
                await engine.Closed.WaitAsync(cancellationToken);
                _engine = null;
                engine.Dispose();
                LogLost();
                if (_time.GetUtcNow() - connected >= Steady)
                {
                    retry = FirstRetry;
                }
            }

            Show(EngineState.Unreachable, problem);
            await Task.Delay(retry, _time, cancellationToken);
            retry = TimeSpan.FromTicks(Math.Min(retry.Ticks * 2, LongestRetry.Ticks));
        }
    }

    private async Task<EngineClient?> ConnectOrStartAsync(CancellationToken cancellationToken)
    {
        if (await EngineClient.ConnectAsync(_paths, ConnectTimeout, cancellationToken) is { } running)
        {
            return running;
        }

        Show(EngineState.Starting);
        using var started = StartEngine();
        var until = _time.GetUtcNow() + StartTimeout;
        while (_time.GetUtcNow() < until)
        {
            if (await EngineClient.ConnectAsync(_paths, ConnectTimeout, cancellationToken) is { } engine)
            {
                return engine;
            }

            // Exited during startup: another engine took the lock first, which one more try finds, or it can't run
            if (started.HasExited)
            {
                LogExited(started.ExitCode);
                return await EngineClient.ConnectAsync(_paths, ConnectTimeout, cancellationToken);
            }
        }

        LogDidntStart(StartTimeout);
        return null;
    }

    // Through the shell, so it inherits none of the app's handles, which would keep whatever started the app waiting
    // for the engine too, and with its console hidden
    private Process StartEngine()
    {
        var obmd = Path.Combine(AppContext.BaseDirectory, "obmd.exe");
        LogStarting(obmd);
        return Process.Start(new ProcessStartInfo(obmd, $"--home \"{_home}\" --started-by-app")
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        })!;
    }

    private void Show(EngineState state, EngineProblem? problem = null) => _ui.Post(
        _ =>
        {
            _status.Problem = problem;
            _status.Engine = state;
        },
        null);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting the engine, {Path}")]
    private partial void LogStarting(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to the engine")]
    private partial void LogConnected();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the connection to the engine")]
    private partial void LogLost();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The engine exited with code {ExitCode} during startup")]
    private partial void LogExited(int exitCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The engine didn't start within {Timeout}")]
    private partial void LogDidntStart(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't reach the engine")]
    private partial void LogCantReach(Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stopped the engine, which the app started")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Left the engine running, as the app didn't start it")]
    private partial void LogLeftRunning();

    [LoggerMessage(Level = LogLevel.Warning, Message = "The engine didn't stop within {Timeout}")]
    private partial void LogDidntStop(TimeSpan timeout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't stop the engine")]
    private partial void LogCouldntStop(Exception exception);
}
