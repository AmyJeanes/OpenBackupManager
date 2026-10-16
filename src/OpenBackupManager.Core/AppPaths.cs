using System.Runtime.InteropServices;

namespace OpenBackupManager.Core;

/// <summary>
/// Where the app keeps its files: config.toml in Config, the engine's state in State, and logs and cache in their own
/// folders. Each OS has its usual folders, or everything goes in one home folder when one is given
/// </summary>
public sealed record AppPaths(string Config, string State, string Logs, string Cache)
{
    public string ConfigFile => Path.Combine(Config, "config.toml");

    // For --home and OBM_HOME
    public static AppPaths InHome(string home) => new(home, home, Path.Combine(home, "logs"), Path.Combine(home, "cache"));

    public static AppPaths ForThisOS() =>
        ForOS(
            OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux,
            Environment.GetEnvironmentVariable);

    // Worked out here rather than with Environment.SpecialFolder, which maps differently on each OS and ignores XDG
    public static AppPaths ForOS(OSPlatform os, Func<string, string?> environment)
    {
        if (os == OSPlatform.Windows)
        {
            var root = Join('\\', Required(environment, "LOCALAPPDATA"), "OpenBackupManager");
            return new(root, root, Join('\\', root, "logs"), Join('\\', root, "cache"));
        }

        var home = Required(environment, "HOME");
        // Config goes where command line tools like rclone and gh keep theirs, which suits dotfiles
        var config = Join('/', Xdg(environment, "XDG_CONFIG_HOME") ?? Join('/', home, ".config"), "openbackupmanager");
        if (os == OSPlatform.OSX)
        {
            return new(
                config,
                Join('/', home, "Library", "Application Support", "OpenBackupManager"),
                Join('/', home, "Library", "Logs", "OpenBackupManager"),
                Join('/', home, "Library", "Caches", "OpenBackupManager"));
        }

        var state = Join('/', Xdg(environment, "XDG_STATE_HOME") ?? Join('/', home, ".local", "state"), "openbackupmanager");
        return new(
            config,
            state,
            Join('/', state, "logs"),
            Join('/', Xdg(environment, "XDG_CACHE_HOME") ?? Join('/', home, ".cache"), "openbackupmanager"));
    }

    private static string Required(Func<string, string?> environment, string name) =>
        environment(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"The {name} environment variable isn't set");

    // The XDG spec says a relative path in these variables is ignored
    private static string? Xdg(Func<string, string?> environment, string name) =>
        environment(name) is { Length: > 0 } value && value.StartsWith('/') ? value : null;

    private static string Join(char separator, params string[] parts) =>
        string.Join(separator, parts.Select((part, i) => i == 0 ? part.TrimEnd(separator) : part.Trim(separator)));
}
