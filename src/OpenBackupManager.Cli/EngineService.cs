using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace OpenBackupManager.Cli;

// What obm service install sets up with the OS's own service manager to keep the engine running, as the user. Each
// home has its own, so homes run side by side as they do by hand
internal abstract class EngineService(string? home, string obmd, Func<string, string?> environment)
{
    // Null for the OS's usual folders
    public string? Home { get; } = home is null ? null : Path.TrimEndingDirectorySeparator(home);

    public string Obmd { get; } = obmd;

    // Where its definition is written
    public abstract string File { get; }

    public bool IsInstalled => System.IO.File.Exists(File);

    // A command that shows what the service manager knows of it, such as why the engine isn't running
    public abstract string HowToCheck { get; }

    // Whether the service manager has stopped starting an engine that keeps failing. launchd never does
    public virtual bool HasGivenUp => false;

    protected Func<string, string?> Environment { get; } = environment;

    // Named from the home's path, so each home's is found again from the same --home
    protected string? HomeKey => Home is null ? null : Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Home)))[..16];

    protected IEnumerable<string> Arguments => [Obmd, "--started-by", "service", .. Home is null ? [] : new[] { "--home", Home }];

    // Null on Windows, where the tray app starts the engine at sign-in
    public static EngineService? ForThisOS(string? home)
    {
        var obmd = Path.Combine(AppContext.BaseDirectory, "obmd");
        return OperatingSystem.IsMacOS() ? new LaunchAgent(home, obmd, System.Environment.GetEnvironmentVariable)
            : OperatingSystem.IsLinux() ? new SystemdService(home, obmd, System.Environment.GetEnvironmentVariable, SystemdService.SystemdEnvironment())
            : null;
    }

    // Writes it and starts the engine, replacing one already there and the engine it started
    public abstract void Install();

    // Stops the engine and removes it
    public abstract void Uninstall();

    // A service gets none of the shell's environment, so it's given the folders obm uses here, or the engine would keep
    // its files somewhere else. A home has everything in it
    protected IEnumerable<(string Name, string Value)> Variables(params string[] names) =>
        Home is not null ? [] : names.Where(name => !string.IsNullOrEmpty(Environment(name))).Select(name => (name, Environment(name)!));

    protected static void Run(string program, params string[] arguments)
    {
        var (exitCode, output) = TryRun(program, arguments);
        if (exitCode != 0)
        {
            throw new ServiceException($"{program} {string.Join(' ', arguments)} failed: {output}");
        }
    }

    // Its exit code and everything it printed
    protected static (int ExitCode, string Output) TryRun(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        try
        {
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, string.Join(' ', new[] { output.Result, errors.Result }.Select(text => text.Trim()).Where(text => text.Length > 0)));
        }
        catch (Win32Exception e)
        {
            throw new ServiceException($"{program} isn't here, so the engine can't be installed as a service", e);
        }
    }
}

internal sealed class ServiceException : Exception
{
    public ServiceException(string message)
        : base(message)
    {
    }

    public ServiceException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
