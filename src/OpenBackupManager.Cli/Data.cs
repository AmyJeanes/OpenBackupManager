using System.Diagnostics;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli;

// obm data remove: everything Open Backup Manager keeps on this device, with its secrets, once the engine has stopped.
// Uninstalling leaves it all, so installing again carries on where it left off
internal static class Data
{
    // What obm, obmd and the app make in a home, as a home is a folder someone chose, which may hold more of theirs
    private static readonly string[] MadeInAHome =
    [
        "config.toml", "config.toml.bak", "config.toml.tmp", "secrets.json", "secrets.json.tmp", "last-version",
        "app.lock", "app.running", "engine.lock", "engine.running",
    ];

    // And in the folders they make there, which may hold theirs too. Nothing writes in cache yet
    private static readonly (string Folder, string[] Files)[] MadeInAHomesFolders =
    [
        ("logs", AppPaths.LogFiles), ("crashes", ["crash-*.txt", "shown"]), ("run", ["engine.sock"]), ("cache", []),
    ];

    public static async Task<int> RemoveAsync(AppPaths paths, string? home, EngineService? service, bool yes, CancellationToken cancellationToken)
    {
        if (Update.InstalledBy(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) == "docker")
        {
            return Fail("in Docker, remove the container instead, then the folder or volume it uses as /config");
        }

        // Taken, so it can't start while the data goes. The app would start the engine again
        ProcessLock? appLock;
        try
        {
            appLock = OperatingSystem.IsWindows() ? ProcessLock.TryTake("app", paths) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail($"the app's lock in {paths.State} can't be taken: {e.Message}. Nothing was removed");
        }

        if (OperatingSystem.IsWindows() && appLock is null)
        {
            return Fail("the app is running, so quit it from its tray icon first");
        }

        using (appLock)
        {
            if (Obmd() is not { } obmd)
            {
                return Fail($"obmd isn't beside obm in {AppContext.BaseDirectory}, so the secrets can't be removed. Nothing was removed");
            }

            Console.WriteLine("This removes, from this device:");
            foreach (var line in home is null ? Folders(paths) : [$"What Open Backup Manager keeps in {home}"])
            {
                Console.WriteLine("  " + line);
            }

            Console.WriteLine("  Its secrets, wherever it kept them");
            if (!yes)
            {
                if (Console.IsInputRedirected)
                {
                    return Fail("--yes is required when not running interactively");
                }

                Console.Write("Remove them? [y/N] ");
                if (Console.ReadLine()?.Trim().ToLowerInvariant() is not ("y" or "yes"))
                {
                    Console.WriteLine("Nothing was removed");
                    return 1;
                }
            }

            try
            {
                await StopAsync(paths, service, cancellationToken);
            }
            catch (Exception e) when (e is ServiceException or EngineException)
            {
                return Fail(e.Message + ". Nothing was removed");
            }

            if (RemoveSecrets(obmd, home) != 0)
            {
                return Fail("the secrets couldn't be removed, so nothing else was either");
            }
        }

        try
        {
            if (home is null)
            {
                foreach (var folder in Folders(paths))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
            else
            {
                RemoveFromHome(home);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Fail($"not everything could be removed: {e.Message}");
        }

        Console.WriteLine("Removed all of Open Backup Manager's data");
        return 0;
    }

    // The usual folders are named for it, so they go whole, but not one inside another, as on Windows
    private static IEnumerable<string> Folders(AppPaths paths)
    {
        var folders = new[] { paths.Config, paths.State, paths.Logs, paths.Cache }.Select(Path.TrimEndingDirectorySeparator).Distinct().ToList();
        return folders.Where(f => Directory.Exists(f) && !folders.Any(other => other != f && f.StartsWith(other + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    // And each folder, and the home itself, if that leaves it empty
    private static void RemoveFromHome(string home)
    {
        foreach (var name in MadeInAHome)
        {
            if (File.Exists(Path.Combine(home, name)))
            {
                File.Delete(Path.Combine(home, name));
            }
        }

        foreach (var (name, files) in MadeInAHomesFolders)
        {
            var folder = Path.Combine(home, name);
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in files.SelectMany(pattern => Directory.GetFiles(folder, pattern)))
            {
                File.Delete(file);
            }

            RemoveIfEmpty(folder);
        }

        RemoveIfEmpty(home);
    }

    private static void RemoveIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
    }

    // Its service is uninstalled, so it doesn't start it again, or one started another way is stopped
    private static async Task StopAsync(AppPaths paths, EngineService? service, CancellationToken cancellationToken)
    {
        if (service is { IsInstalled: true })
        {
            service.Uninstall();
            Console.WriteLine("The engine has stopped, and no longer starts when you sign in");
            return;
        }

        using var engine = await EngineClient.ConnectAsync(paths, cancellationToken);
        if (engine is not null)
        {
            await Service.StopAsync(engine, (await engine.StatusAsync(cancellationToken)).ProcessId, cancellationToken);
            Console.WriteLine("The engine has stopped");
        }
    }

    // obmd has the OS's stores, which obm doesn't. It says what it removed, which is passed on
    private static int RemoveSecrets(string obmd, string? home)
    {
        var start = new ProcessStartInfo(obmd, ["--remove-secrets", .. home is null ? Array.Empty<string>() : ["--home", home]])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        var errors = stderr.ReadToEndAsync();
        Console.Write(stdout.ReadToEnd());
        process.WaitForExit();
        Console.Error.Write(errors.Result);
        return process.ExitCode;
    }

    // Beside obm, or on Windows, the folder above, as the app keeps obm in bin
    private static string? Obmd()
    {
        var name = OperatingSystem.IsWindows() ? "obmd.exe" : "obmd";
        return new[] { Path.Combine(AppContext.BaseDirectory, name), Path.Combine(AppContext.BaseDirectory, "..", name) }
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("obm: " + message);
        return 1;
    }
}
