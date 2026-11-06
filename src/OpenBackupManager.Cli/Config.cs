using System.Globalization;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm config: has the engine read config.toml again, or checks a file without it
internal static class Config
{
    public static async Task<int> ReloadAsync(AppPaths paths, bool json, CancellationToken cancellationToken)
    {
        try
        {
            using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
            if (engine is null)
            {
                await Console.Error.WriteLineAsync("obm: the engine isn't running, so there's nothing to reload");
                return 1;
            }

            var result = await engine.ReloadConfigAsync(cancellationToken);
            var problems = result.Problems;
            return Print(
                json ? [Json.Format(result, ProtocolJson.Default.ReloadConfigResult)]
                : problems.Count == 0 ? ["config.toml's settings are applied"]
                : Status.ConfigProblems(problems, CultureInfo.CurrentUICulture),
                problems,
                Console.Out);
        }
        catch (EngineException e)
        {
            await Console.Error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    // On its own by default, as a service, the app or CI would read it, as none of them has this shell's variables
    public static int Validate(AppPaths paths, string? path, bool withEnvironment, bool json, TextWriter output, TextWriter error)
    {
        var file = new ConfigFile(path is null ? paths.ConfigFile : Path.GetFullPath(path), withEnvironment ? Environment.GetEnvironmentVariable : _ => null);
        if (path is not null && !File.Exists(file.Path))
        {
            error.WriteLine($"obm: there's no file at {file.Path}");
            return 1;
        }

        IReadOnlyList<ConfigProblem> problems;
        try
        {
            problems = file.Read().Problems;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"obm: {file.Path} can't be read: {e.Message}");
            return 1;
        }

        var name = path is null ? "config.toml" : file.Path;
        // As reload gives it, so a script reads both the same way
        return Print(
            json ? [Json.Format(new ReloadConfigResult(problems), ProtocolJson.Default.ReloadConfigResult)]
            : problems.Count == 0 ? [$"{name} has no errors or warnings"]
            : [$"{name} has {Status.Count(problems)}:", .. Status.Lines(problems, CultureInfo.CurrentUICulture)],
            problems,
            output);
    }

    private static int Print(IEnumerable<string> lines, IReadOnlyList<ConfigProblem> problems, TextWriter output)
    {
        foreach (var line in lines)
        {
            output.WriteLine(line);
        }

        return problems.Any(p => p.IsError) ? 1 : 0;
    }
}
