using System.Diagnostics;
using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm service install and uninstall: the engine from sign-in on macOS and Linux, through the OS's service manager
internal static class Service
{
    public const string OnWindows = "obm: on Windows, the app starts the engine when you sign in. To run the engine without the app, start obmd with Task Scheduler instead";

    // Long enough for an engine that's just starting
    private static readonly TimeSpan FindTimeout = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> InstallAsync(AppPaths paths, EngineService service, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(service.Obmd))
            {
                throw new ServiceException($"obmd isn't beside obm, at {service.Obmd}, so there's no engine to install");
            }

            await TakeOverAsync(paths, cancellationToken);
            service.Install();
            if (!await StartedAsync(paths, service, cancellationToken))
            {
                throw new ServiceException($"the service didn't start the engine. {service.HowToCheck} might say why");
            }

            if (service is SystemdService && SystemdService.Lingering)
            {
                Console.WriteLine("The engine is running, and starts whenever the computer does");
            }
            else
            {
                Console.WriteLine("The engine is running, and starts whenever you sign in");
                if (service is SystemdService)
                {
                    Console.WriteLine("It stops once you've signed out everywhere. To keep it running, as on a server, run loginctl enable-linger");
                }
            }

            return 0;
        }
        catch (Exception e) when (e is ServiceException or EngineException)
        {
            await Console.Error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    public static int Uninstall(EngineService service)
    {
        if (!service.IsInstalled)
        {
            Console.WriteLine("The engine isn't installed as a service");
            return 0;
        }

        try
        {
            service.Uninstall();
        }
        catch (ServiceException e)
        {
            Console.Error.WriteLine("obm: " + e.Message);
            return 1;
        }

        Console.WriteLine("The engine has stopped, and no longer starts when you sign in");
        return 0;
    }

    // An engine started another way would keep the service's from starting, as only one runs for each home. The
    // service's own is left for installing again to restart
    private static async Task TakeOverAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        using var engine = await EngineClient.ConnectAsync(paths, FindTimeout, cancellationToken);
        if (engine is null)
        {
            return;
        }

        var status = await engine.StatusAsync(cancellationToken);
        if (status.StartedBy == EngineStarter.Service)
        {
            return;
        }

        Console.WriteLine("Stopping the engine that's running, so the service can start it");
        using var process = Process.GetProcessById(status.ProcessId);
        await engine.StopAsync(cancellationToken);
        // It closes the connection as it stops, but holds its lock until it exits
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ServiceException(string.Create(CultureInfo.InvariantCulture, $"the engine that's running didn't stop within {StartTimeout.TotalSeconds} seconds"));
        }
    }

    // A few seconds at a time, so it can stop waiting once the service manager stops trying
    private static async Task<bool> StartedAsync(AppPaths paths, EngineService service, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + StartTimeout;
        while (DateTime.UtcNow < until && !service.HasGivenUp)
        {
            using var engine = await EngineClient.ConnectAsync(paths, FindTimeout, cancellationToken);
            if (engine is not null)
            {
                return (await engine.StatusAsync(cancellationToken)).StartedBy == EngineStarter.Service;
            }

            // Connecting gives up at once on a socket path too long for the OS
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }
}
