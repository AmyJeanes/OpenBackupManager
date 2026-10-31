using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm status: whether the engine is running, and which
internal static class Status
{
    // Long enough for an engine that's just starting
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    // The service, on macOS and Linux, is only for saying when it should have started the engine
    public static async Task<int> RunAsync(AppPaths paths, EngineService? service, CancellationToken cancellationToken)
    {
        try
        {
            using var engine = await EngineClient.ConnectAsync(paths, Timeout, cancellationToken);
            if (engine is null)
            {
                await Console.Error.WriteLineAsync(
                    service is { SwitchedOff: true } ? $"obm: the engine isn't running, as obmd is switched off in {service.WhereToSwitchOn}. Switching it on there starts it"
                    : service is { IsInstalled: true } ? $"obm: the engine isn't running, though it's installed as a service. {service.HowToCheck} might say why"
                    : "obm: the engine isn't running");
                return 1;
            }

            var status = await engine.StatusAsync(cancellationToken);
            Console.WriteLine("The engine is running");
            Console.WriteLine($"  Version: {status.Version}");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Process: {status.ProcessId}"));
            Console.WriteLine($"  Up for: {Uptime(DateTimeOffset.UtcNow - status.Started)}");
            Console.WriteLine("  Started by: " + StartedBy(status.StartedBy));
            if (status.Config == status.State)
            {
                Console.WriteLine($"  Home: {status.Config}");
            }
            else
            {
                Console.WriteLine($"  Config: {status.Config}");
                Console.WriteLine($"  State: {status.State}");
            }

            return 0;
        }
        catch (EngineException e)
        {
            await Console.Error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    private static string StartedBy(EngineStarter starter) => starter switch
    {
        EngineStarter.App => "the app, which stops it when you quit",
        EngineStarter.Service => "its service, from obm service install",
        _ => "you or another program",
    };

    // Such as 2d 3h 4m 5s, from the first part that isn't zero
    internal static string Uptime(TimeSpan up)
    {
        var parts = new[] { (up.Days, "d"), (up.Hours, "h"), (up.Minutes, "m"), (up.Seconds, "s") }
            .SkipWhile(part => part.Item1 == 0)
            .Select(part => string.Create(CultureInfo.InvariantCulture, $"{part.Item1}{part.Item2}"))
            .ToList();
        return parts.Count == 0 ? "0s" : string.Join(' ', parts);
    }
}
