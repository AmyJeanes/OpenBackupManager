using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;
using Velopack;

namespace OpenBackupManager.Cli;

// obm update: replaces obm and obmd where they are with the newest release, on macOS and Linux. The engine's
// UpdateWatcher notices the new obmd, and a service restarts it into it
internal static class Update
{
    private static readonly TimeSpan RestartTimeout = TimeSpan.FromSeconds(30);

    public static async Task<int> RunAsync(AppPaths paths, EngineService? service, bool check, CancellationToken cancellationToken)
    {
        try
        {
            var source = Environment.GetEnvironmentVariable("OBM_UPDATE_SOURCE") is { Length: > 0 } value ? value : null;
            var current = SemanticVersion.Parse(AppVersion.Current.Split('+')[0]);
            // Unless it's being tried against a local source, where whoever built it chose what it updates to
            if (current == SemanticVersion.Parse("0.0.0") && source is null)
            {
                throw new UpdateException("this is a local development build (0.0.0), so obm update won't replace it with a release");
            }

            using var releases = new Releases(source);
            var newest = await releases.NewestAsync(Prereleases(paths, current), cancellationToken);
            // Never back to a lower version, such as a release from a test version that's newer
            if (newest is null || newest.Version <= current)
            {
                Console.WriteLine($"This is the newest version, {current}");
                return 0;
            }

            var folder = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            if (HowToUpdateInstead(folder, current, newest.Version) is { } instead)
            {
                await Console.Error.WriteLineAsync("obm: " + instead);
                return check ? 0 : 1;
            }

            if (check)
            {
                Console.WriteLine($"{newest.Version} is out, and this is {current}. Run obm update to update");
                return 0;
            }

            if (!File.Exists(Path.Combine(folder, "obmd")))
            {
                throw new UpdateException($"obmd isn't beside obm in {folder}, so they can't be updated together. Unpack the new version yourself");
            }

            if (!CanWrite(folder))
            {
                throw new UpdateException(
                    $"can't write to {folder}, so obm and obmd can't be updated there. Unpack the new version there yourself, or move them to a folder of yours, such as ~/.local/bin");
            }

            var name = TarballName(newest.Version);
            var asset = newest.Assets.FirstOrDefault(a => a.Name == name)
                ?? throw new UpdateException($"{newest.Version} has no download for {Runtime} yet, so nothing was changed. Try again later");
            if (asset.Digest is not { } digest || !digest.StartsWith("sha256:", StringComparison.Ordinal))
            {
                throw new UpdateException($"GitHub doesn't give a SHA-256 hash for {name}, so it can't be checked, and nothing was changed");
            }

            Console.WriteLine($"Updating from {current} to {newest.Version}");
            // Noted before obmd is replaced, as the engine may restart soon after
            var before = await RunningAsync(paths, cancellationToken);
            var tarball = Path.Combine(Path.GetTempPath(), $"obm-update-{Guid.NewGuid():N}.tar.gz");
            try
            {
                var hash = await releases.DownloadAsync(asset, tarball, cancellationToken);
                if (!string.Equals(Convert.ToHexStringLower(hash), digest["sha256:".Length..], StringComparison.OrdinalIgnoreCase))
                {
                    throw new UpdateException($"the hash check failed: {name}'s SHA-256 hash isn't the one GitHub gives for it, so nothing was changed. Try again, and report it if it keeps happening");
                }

                Console.WriteLine($"Downloaded {name}, and its SHA-256 hash matches the one GitHub gives for it");
                Replace(tarball, folder, name);
            }
            finally
            {
                File.Delete(tarball);
            }

            Console.WriteLine($"Updated obm and obmd in {folder}");
            return await RestartedAsync(paths, service, before, newest.Version, cancellationToken);
        }
        catch (Exception e) when (e is UpdateException or EngineException)
        {
            await Console.Error.WriteLineAsync("obm: " + e.Message);
            return 1;
        }
    }

    private static string Runtime =>
        (OperatingSystem.IsMacOS() ? "osx-" : "linux-") + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

    private static string TarballName(SemanticVersion version) => $"OpenBackupManager-{version}-{Runtime}.tar.gz";

    // As the app's own updates do: set in the settings file, or else test versions get test versions
    private static bool Prereleases(AppPaths paths, SemanticVersion current)
    {
        try
        {
            return new ConfigFile(paths.ConfigFile, Environment.GetEnvironmentVariable).Read().Config?.Updates.Prereleases ?? current.IsPrerelease;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return current.IsPrerelease;
        }
    }

    // From installed-by, which a package or image puts beside the programs, as they're updated another way. A tarball
    // has none
    private static string? HowToUpdateInstead(string folder, SemanticVersion current, SemanticVersion newest)
    {
        var file = Path.Combine(folder, "installed-by");
        if (!File.Exists(file))
        {
            return null;
        }

        var by = File.ReadAllText(file).Trim();
        var download = $"https://github.com/AmyJeanes/OpenBackupManager/releases/download/v{newest}/OpenBackupManager-{newest}-{Runtime}";
        var nl = Environment.NewLine;
        var how = by switch
        {
            "deb" => $"It was installed from a .deb, which needs root to update, so run:{nl}  curl -LO {download}.deb{nl}  sudo apt install ./OpenBackupManager-{newest}-{Runtime}.deb",
            "rpm" => $"It was installed from a .rpm, which needs root to update, so run:{nl}  sudo dnf install {download}.rpm",
            "docker" => "In Docker, update the image instead, with docker compose pull and docker compose up -d, after changing its image tag if it's an explicit version, such as 0.2.0",
            _ => $"It was installed by {by}, so update it the same way",
        };
        return $"{newest} is out, and this is {current}. {how}";
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            using var _ = File.Create(Path.Combine(folder, $".obm-update-{Guid.NewGuid():N}"), 1, FileOptions.DeleteOnClose);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Each file unpacked beside the one it replaces, then renamed over it, so a program is never half written. obmd
    // goes last, as the engine acts on it
    private static void Replace(string tarball, string folder, string name)
    {
        var unpacked = new List<(string From, string To)>();
        try
        {
            using (var file = File.OpenRead(tarball))
            using (var gzip = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new TarReader(gzip))
            {
                while (reader.GetNextEntry() is { } entry)
                {
                    // Only files at its top level, as a release has, so nothing lands outside the folder. The licences
                    // only if they were unpacked here too, as the guide unpacks only the programs into a folder on PATH
                    var to = Path.Combine(folder, entry.Name);
                    if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.Name.Contains('/')
                        || entry.Name is not ("obm" or "obmd") && !File.Exists(to))
                    {
                        continue;
                    }

                    var from = Path.Combine(folder, $".{entry.Name}.update");
                    entry.ExtractToFile(from, overwrite: true);
                    unpacked.Add((from, to));
                }
            }

            if (!unpacked.Any(f => Path.GetFileName(f.To) == "obm") || !unpacked.Any(f => Path.GetFileName(f.To) == "obmd"))
            {
                throw new UpdateException($"{name} doesn't have obm and obmd in it, so nothing was changed");
            }

            foreach (var (from, to) in unpacked.OrderBy(f => Path.GetFileName(f.To) == "obmd"))
            {
                File.Move(from, to, overwrite: true);
            }
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new UpdateException($"couldn't unpack {name}: {e.Message}", e);
        }
        finally
        {
            foreach (var (from, _) in unpacked)
            {
                File.Delete(from);
            }
        }
    }

    // Null if no engine is running
    private static async Task<StatusResult?> RunningAsync(AppPaths paths, CancellationToken cancellationToken)
    {
        using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
        return engine is null ? null : await engine.StatusAsync(cancellationToken);
    }

    // A service restarts the engine into the new obmd once its UpdateWatcher notices it. One started another way only
    // says so, as nothing would start it again
    private static async Task<int> RestartedAsync(AppPaths paths, EngineService? service, StatusResult? before, SemanticVersion version, CancellationToken cancellationToken)
    {
        if (before is null)
        {
            Console.WriteLine($"The engine isn't running, so it'll run {version} when it next starts");
            return 0;
        }

        if (before.StartedBy != EngineStarter.Service)
        {
            Console.WriteLine($"The engine is still running {before.Version.Split('+')[0]}. Restart it to run {version}");
            return 0;
        }

        Console.WriteLine("Waiting for the service to restart the engine");
        var until = DateTime.UtcNow + RestartTimeout;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            try
            {
                if (await RunningAsync(paths, cancellationToken) is { } now && now.ProcessId != before.ProcessId)
                {
                    Console.WriteLine($"The engine restarted and is running {now.Version.Split('+')[0]}");
                    return 0;
                }
            }
            // As it stops, it can close the connection before answering
            catch (EngineException)
            {
            }
            catch (IOException)
            {
            }
        }

        throw new UpdateException($"the engine didn't come back within {RestartTimeout.TotalSeconds:0} seconds. {service?.HowToCheck ?? "Its service"} might say why");
    }
}
