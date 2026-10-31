namespace OpenBackupManager.Cli;

// A systemd user service on Linux, which runs while the user has a session, or all the time with lingering on.
// systemdEnvironment is systemd's own, which can differ from the shell's
internal sealed class SystemdService(string? home, string obmd, Func<string, string?> environment, Func<string, string?> systemdEnvironment)
    : EngineService(home, obmd, environment)
{
    public string Unit => HomeKey is null ? "openbackupmanager.service" : $"openbackupmanager-{HomeKey}.service";

    public override string File => Path.Combine(ConfigFolder, "systemd", "user", Unit);

    public override string HowToCheck => "systemctl --user status " + Unit;

    // After five failed starts in ten seconds, by systemd's defaults
    public override bool HasGivenUp => TryRun("systemctl", "--user", "is-failed", "--quiet", Unit).ExitCode == 0;

    // Off, the engine stops once the user's last session ends, until they sign in again
    public static bool Lingering => System.IO.File.Exists("/var/lib/systemd/linger/" + System.Environment.UserName);

    // Where systemd looks for the user's own units, by its own XDG_CONFIG_HOME rather than one set only in the shell
    private string ConfigFolder =>
        systemdEnvironment("XDG_CONFIG_HOME") is { Length: > 0 } config && config.StartsWith('/') ? config : Path.Combine(Environment("HOME")!, ".config");

    // Asked for once it's needed. Empty where systemd isn't running, which installing then reports
    public static Func<string, string?> SystemdEnvironment()
    {
        var variables = new Lazy<Dictionary<string, string>>(() =>
        {
            try
            {
                var (exitCode, output) = TryRun("systemctl", "--user", "show-environment");
                return exitCode == 0 ? ParseEnvironment(output) : [];
            }
            catch (ServiceException)
            {
                return [];
            }
        });
        return name => variables.Value.GetValueOrDefault(name);
    }

    // One NAME=value to a line
    internal static Dictionary<string, string> ParseEnvironment(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .DistinctBy(parts => parts[0])
            .ToDictionary(parts => parts[0], parts => parts[1]);

    public override void Install()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, Definition());
        Run("systemctl", "--user", "daemon-reload");
        Run("systemctl", "--user", "enable", Unit);
        // Restarted rather than started, so an engine it started before runs as it's now defined
        Run("systemctl", "--user", "restart", Unit);
    }

    public override void Uninstall()
    {
        Run("systemctl", "--user", "disable", "--now", Unit);
        System.IO.File.Delete(File);
        Run("systemctl", "--user", "daemon-reload");
    }

    // Started again if it crashes or fails, but not once it's stopped on purpose
    internal string Definition()
    {
        var lines = new List<string>
        {
            "# Written by obm service install, and removed by obm service uninstall",
            "[Unit]",
            "Description=Open Backup Manager's engine",
            "",
            "[Service]",
            "ExecStart=" + string.Join(' ', Arguments.Select(argument => Quote(argument, isCommand: true))),
        };
        lines.AddRange(Variables("XDG_CONFIG_HOME", "XDG_STATE_HOME", "XDG_CACHE_HOME").Select(variable => "Environment=" + Quote($"{variable.Name}={variable.Value}", isCommand: false)));
        lines.AddRange(["Restart=on-failure", "", "[Install]", "WantedBy=default.target", ""]);
        return string.Join('\n', lines);
    }

    // systemd reads % as the start of a specifier, and $ in a command as a variable
    private static string Quote(string value, bool isCommand)
    {
        var quoted = value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("%", "%%", StringComparison.Ordinal);
        return '"' + (isCommand ? quoted.Replace("$", "$$", StringComparison.Ordinal) : quoted) + '"';
    }
}
