using System.Globalization;
using System.Runtime.InteropServices;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm status: whether the engine is running, and which, and any crash reports not yet seen
internal static class Status
{
    // The service, on macOS and Linux, is only for saying when it should have started the engine. Crash reports are
    // listed whether it's running or not, as they may say why it isn't. Scripts get them from obm crashes list
    public static async Task<int> RunAsync(AppPaths paths, EngineService? service, bool json, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var exitCode = await EngineAsync(paths, service, json, output, error, cancellationToken);
        if (!json)
        {
            foreach (var line in Crashes.New(paths))
            {
                output.WriteLine(line);
            }
        }

        return exitCode;
    }

    private static async Task<int> EngineAsync(AppPaths paths, EngineService? service, bool json, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
            if (engine is null)
            {
                await error.WriteLineAsync(
                    service is { SwitchedOff: true } ? $"obm: the engine isn't running, as obmd is switched off in {service.WhereToSwitchOn}. Switching it on there starts it"
                    : service is { IsInstalled: true } ? $"obm: the engine isn't running, though it's installed as a service. {service.HowToCheck} might say why"
                    : "obm: the engine isn't running");
                return 1;
            }

            var status = await engine.StatusAsync(cancellationToken);
            if (json)
            {
                output.WriteLine(Json.Format(status, ProtocolJson.Default.StatusResult));
                return 0;
            }

            output.WriteLine("The engine is running");
            output.WriteLine($"  Version: {status.Version}");
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  Process: {status.ProcessId}"));
            output.WriteLine($"  Up for: {Uptime(DateTimeOffset.UtcNow - status.Started)}");
            output.WriteLine("  Started by: " + StartedBy(status.StartedBy));
            if (status.Config == status.State)
            {
                output.WriteLine($"  Home: {status.Config}");
            }
            else
            {
                output.WriteLine($"  Config: {status.Config}");
                output.WriteLine($"  State: {status.State}");
            }

            output.WriteLine("  Secrets: " + Secrets(status.Secrets, CurrentOS.Platform));
            foreach (var line in ConfigProblems(status.ConfigProblems, CultureInfo.CurrentUICulture))
            {
                output.WriteLine("  " + line);
            }

            return 0;
        }
        catch (EngineException e)
        {
            await error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    private static string StartedBy(EngineStarter starter) => starter switch
    {
        EngineStarter.App => "the app, which stops it when you quit",
        EngineStarter.Service => "its service, from obm service install",
        _ => "you or another program",
    };

    internal static string Secrets(SecretsStatus secrets, OSPlatform os)
    {
        if (secrets.Store is null)
        {
            return "no store chosen yet. Choose one with secret-store in config.toml or OBM_SECRET_STORE: "
                + string.Join(" or ", SecretStoreKinds.NamesOn(os));
        }

        return secrets.File is { } file
            ? $"in {file}, protected only by its permissions"
            : "in " + SecretStoreKinds.Parse(secrets.Store)!.Value.Title();
    }

    internal static IEnumerable<string> ConfigProblems(IReadOnlyList<ConfigProblem> problems, CultureInfo culture)
    {
        if (problems.Count == 0)
        {
            return [];
        }

        var heading = problems.Any(p => p.IsError)
            ? $"config.toml has {Count(problems)}, so its latest changes aren't applied:"
            : $"config.toml has {Count(problems)}, but its settings are applied:";
        return [heading, .. Lines(problems, culture)];
    }

    // Such as "an error" or "warnings". Errors are what matter, so warnings are only counted when there are none
    internal static string Count(IReadOnlyList<ConfigProblem> problems)
    {
        var errors = problems.Count(p => p.IsError);
        return errors > 0 ? (errors == 1 ? "an error" : "errors") : (problems.Count == 1 ? "a warning" : "warnings");
    }

    // Each indented under a heading, errors first
    internal static IEnumerable<string> Lines(IReadOnlyList<ConfigProblem> problems, CultureInfo culture) =>
        problems.OrderBy(p => !p.IsError).Select(p => "  " + Describe(p, culture));

    private static string Describe(ConfigProblem problem, CultureInfo culture) =>
        (problem.IsError ? "Error" : "Warning")
        + (problem.EnvVar is { } envVar ? $" in {envVar}" : string.Create(CultureInfo.InvariantCulture, $" on line {problem.Line}"))
        + ": " + problem.Message(culture);

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
