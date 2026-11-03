using System.Diagnostics;
using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm service install and uninstall: the engine from sign-in on macOS and Linux, through the OS's service manager
internal static class Service
{
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
            var status = await StartedAsync(paths, service, cancellationToken)
                ?? throw new ServiceException($"the service didn't start the engine. {service.HowToCheck} might say why");
            var inSecretService = status.Secrets.Store == SecretStoreKind.SecretService.Name();

            if (service.SwitchedOff)
            {
                Console.WriteLine($"The engine is running, but won't start when you sign in, as obmd is switched off in {service.WhereToSwitchOn}");
            }
            else if (service is SystemdService && SystemdService.Lingering)
            {
                Console.WriteLine("The engine is running, and starts whenever the computer does");
                if (inSecretService)
                {
                    Console.WriteLine(SystemdService.LockedWhileSignedOut);
                }
            }
            else
            {
                Console.WriteLine("The engine is running, and starts whenever you sign in");
                if (service is SystemdService)
                {
                    Console.WriteLine("It stops once you've signed out everywhere. To keep it running, as on a server, run loginctl enable-linger");
                    if (inSecretService)
                    {
                        Console.WriteLine(SystemdService.LockedWhileSignedOut);
                    }
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
        using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
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

    // A few seconds at a time, so it can stop waiting once the service manager stops trying. Null if it didn't start it
    private static async Task<StatusResult?> StartedAsync(AppPaths paths, EngineService service, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + StartTimeout;
        while (DateTime.UtcNow < until && !service.HasGivenUp)
        {
            using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
            if (engine is not null)
            {
                var status = await engine.StatusAsync(cancellationToken);
                return status.StartedBy == EngineStarter.Service ? status : null;
            }

            // Connecting gives up at once on a socket path too long for the OS
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return null;
    }
}
