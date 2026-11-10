using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Engine;

// Notices when an update, such as a .deb or .rpm, has replaced obmd under the running engine. Its service restarts it
// into the new one, but an engine started another way only says so, as nothing would start it again. If obmd is removed,
// the engine removes its service, if it has one, and stops. Not on Windows, where Velopack ends whatever runs from its
// folder before updating it
public sealed partial class UpdateWatcher(
    EngineOptions options,
    IHostApplicationLifetime lifetime,
    TimeProvider time,
    ILogger<UpdateWatcher> logger) : BackgroundService
{
    // EX_TEMPFAIL, which the service restarts without counting as a failure
    public const int RestartExitCode = 75;

    // Checked rather than watched: works on every file system, and a file still being written needs a timer anyway
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(10);

    public int ExitCode { get; private set; }

    // The version an update installed, while this engine still runs the one before it
    public string? Installed { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not when run through dotnet, as in development
        if (OperatingSystem.IsWindows() || Environment.ProcessPath is not { } path || Path.GetFileName(path) != "obmd")
        {
            return;
        }

        // Where it really is, when run through a link such as /usr/bin/obmd
        var obmd = new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        var running = Identity(obmd);
        var last = running;
        using var timer = new PeriodicTimer(CheckEvery, time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = Identity(obmd);
            // Only once it's the same at two checks in a row, so a file still being written isn't run
            if (now != last || now == running)
            {
                last = now;
                continue;
            }

            if (now is null)
            {
                LogRemoved(obmd);
                if (options.ServiceFile is { } service)
                {
                    RemoveService(service);
                }

                Stop(0);
                return;
            }

            running = now;
            if (Version(obmd) is not { } version)
            {
                continue;
            }

            if (options.StartedBy == EngineStarter.Service)
            {
                LogRestarting(version);
                Stop(RestartExitCode);
                return;
            }

            Installed = version;
            LogNeedsRestart(version);
        }
    }

    private void Stop(int exitCode)
    {
        ExitCode = exitCode;
        lifetime.StopApplication();
    }

    // Or the service manager would fail to start it at each sign-in, such as once its package is removed, and only the
    // engine knows it's there. It's not stopped through the service manager, which would stop this engine as it stops;
    // the engine exits with 0, which neither restarts. The file goes even if systemd can't be told, as without it
    // there's nothing to start
    private void RemoveService(string file)
    {
        var problem = OperatingSystem.IsLinux() ? Systemctl("disable", Path.GetFileName(file)) : null;
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            LogServiceNotRemoved(file, e.Message);
            return;
        }

        problem ??= OperatingSystem.IsLinux() ? Systemctl("daemon-reload") : null;
        if (problem is not null)
        {
            LogServiceNotRemoved(file, problem);
            return;
        }

        LogServiceRemoved(file);
    }

    // What went wrong, or null
    private static string? Systemctl(params string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(SystemPrograms.Find("systemctl"), ["--user", .. arguments])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = Process.Start(start)!;
            using var stdout = process.StandardOutput;
            using var stderr = process.StandardError;
            var output = stdout.ReadToEndAsync();
            var errors = stderr.ReadToEndAsync();
            if (!process.WaitForExit(VersionTimeout))
            {
                process.Kill();
                return $"systemctl --user {string.Join(' ', arguments)} didn't finish";
            }

            return process.ExitCode == 0 ? null : $"systemctl --user {string.Join(' ', arguments)} failed: {(errors.Result + output.Result).Trim()}";
        }
        catch (Win32Exception e)
        {
            return e.Message;
        }
    }

    // An update brings a file built at another time, so it's told apart without reading all of it. Null once it's gone
    private static (DateTime, long)? Identity(string path)
    {
        var file = new FileInfo(path);
        return file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
    }

    // From the new obmd itself. Null if it doesn't run, so the engine carries on with the one it has
    private string? Version(string path)
    {
        var start = new ProcessStartInfo(path, ["--version"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(start)!;
            using var stdout = process.StandardOutput;
            using var stderr = process.StandardError;
            var errors = stderr.ReadToEndAsync();
            var output = stdout.ReadToEndAsync();
            if (!process.WaitForExit(VersionTimeout))
            {
                process.Kill();
                LogDoesntRun(path, "it didn't finish");
                return null;
            }

            if (process.ExitCode != 0)
            {
                LogDoesntRun(path, errors.Result.Trim());
                return null;
            }

            return output.Result.Trim();
        }
        catch (Win32Exception e)
        {
            LogDoesntRun(path, e.Message);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "obmd {Version} is installed, so the engine is restarting to run it")]
    private partial void LogRestarting(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "obmd {Version} is installed, but this engine runs the version before it until it's restarted")]
    private partial void LogNeedsRestart(string version);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} has been replaced, but the new one doesn't run, so the engine carries on as it is: {Reason}")]
    private partial void LogDoesntRun(string path, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Path} has been removed, so the engine is stopping")]
    private partial void LogRemoved(string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed the engine's service, {File}, so it isn't started again")]
    private partial void LogServiceRemoved(string file);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The engine's service, {File}, may not be removed completely, so the service manager may try to start it at each sign-in: {Reason}")]
    private partial void LogServiceNotRemoved(string file, string reason);
}
