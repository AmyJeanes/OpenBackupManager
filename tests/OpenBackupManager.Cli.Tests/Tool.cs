using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace OpenBackupManager.Cli.Tests;

// The built obm or obmd, run as a user would, with what it prints collected
internal sealed class Tool : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _name;
    private readonly Process _process;
    private readonly List<string> _lines = [];
    private readonly List<string> _stdout = [];
    private readonly List<string> _stderr = [];
    private bool _disposed;

    private Tool(string name, Process process)
    {
        _name = name;
        _process = process;
    }

    // A folder from tools/Publish-App.ps1, which CI tests instead of the build, as it's what ships
    public static string? Published { get; } = Environment.GetEnvironmentVariable("OBM_TEST_PUBLISHED") is { Length: > 0 } published ? published : null;

    public int Id => _process.Id;

    public bool HasExited => _process.HasExited;

    // Both what it prints and its errors, in the order they came
    public string Output
    {
        get
        {
            lock (_lines)
            {
                return string.Join(Environment.NewLine, _lines);
            }
        }
    }

    // Only what it prints to stdout, as a script reading it gets
    public string Stdout
    {
        get
        {
            lock (_lines)
            {
                return string.Join(Environment.NewLine, _stdout);
            }
        }
    }

    public string Stderr
    {
        get
        {
            lock (_lines)
            {
                return string.Join(Environment.NewLine, _stderr);
            }
        }
    }

    // Name is obm or obmd, or the path of a copy from CopyTo
    public static Tool Start(string name, params string[] args) => Start(name, new Dictionary<string, string>(), args);

    // With these environment variables set too
    public static Tool Start(string name, IReadOnlyDictionary<string, string> environment, params string[] args) =>
        StartIn(null, name, environment, args);

    // Run from this folder rather than the tests'
    public static (int ExitCode, string Output) RunIn(string folder, string name, params string[] args)
    {
        using var tool = StartIn(folder, name, new Dictionary<string, string>(), args);
        return (tool.WaitForExit(), tool.Output);
    }

    private static Tool StartIn(string? folder, string name, IReadOnlyDictionary<string, string> environment, string[] args)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var path = Path.IsPathRooted(name) ? name
            : Published is null ? Path.Combine(AppContext.BaseDirectory, file)
            : name == "obm" && OperatingSystem.IsWindows() ? Path.Combine(Published, "bin", file)
            : Path.Combine(Published, file);
        // No terminal to read from, as in CI, whether or not the tests run in one
        var start = new ProcessStartInfo(path, args)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Windows opens a console window for each otherwise, when the test host has none of its own
            CreateNoWindow = true,
            WorkingDirectory = folder ?? "",
        };
        // Only the test's own settings, rather than any exported where the tests run
        foreach (var variable in start.Environment.Keys.Where(key => key.StartsWith("OBM_", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(variable);
        }

        // On macOS and Linux, the .NET runtime's debugger pipes and diagnostics socket go in the temp folder, and are
        // left there when a test kills the program. Windows' go with the program, so a debugger can still attach there
        if (!OperatingSystem.IsWindows())
        {
            start.Environment["DOTNET_EnableDiagnostics"] = "0";
        }

        foreach (var (variable, value) in environment)
        {
            start.Environment[variable] = value;
        }

        var tool = new Tool(name, new Process { StartInfo = start });
        tool._process.OutputDataReceived += (_, e) => tool.Add(e.Data, stdout: true);
        tool._process.ErrorDataReceived += (_, e) => tool.Add(e.Data, stdout: false);
        tool._process.Start();
        tool._process.StandardInput.Close();
        tool._process.BeginOutputReadLine();
        tool._process.BeginErrorReadLine();
        return tool;
    }

    // obm, obmd and obm-rclone on macOS and Linux, in a folder of their own that a test can change as an update would.
    // The build needs everything beside them, but what's published is only them
    public static void CopyTo(string folder)
    {
        var from = Published ?? AppContext.BaseDirectory;
        var files = Published is null ? Directory.GetFiles(from, "*", SearchOption.AllDirectories) : [Path.Combine(from, "obm"), Path.Combine(from, "obmd"), Path.Combine(from, "obm-rclone")];
        foreach (var file in files)
        {
            var to = Path.Combine(folder, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
    }

    // As a package manager does, by renaming a newer file over it
    public static void Update(string file)
    {
        var next = file + ".new";
        File.Copy(file, next);
        File.SetLastWriteTimeUtc(next, File.GetLastWriteTimeUtc(file).AddMinutes(1));
        File.Move(next, file, overwrite: true);
    }

    // Runs it to the end
    public static (int ExitCode, string Output) Run(string name, params string[] args) => Run(name, new Dictionary<string, string>(), args);

    public static (int ExitCode, string Output) Run(string name, IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        using var tool = Start(name, environment, args);
        return (tool.WaitForExit(), tool.Output);
    }

    public void WaitFor(string text)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!Output.Contains(text, StringComparison.Ordinal))
        {
            if (_process.HasExited || DateTime.UtcNow > until)
            {
                Assert.Fail($"{_name} didn't print \"{text}\". It printed:{Environment.NewLine}{Output}");
                // Inside Assert.EnterMultipleScope, failing doesn't end the test
                return;
            }

            Thread.Sleep(100);
        }
    }

    public int WaitForExit()
    {
        if (!_process.WaitForExit(Timeout))
        {
            // Ended first, since inside Assert.EnterMultipleScope failing doesn't stop the wait below
            Kill();
            Assert.Fail($"{_name} didn't exit. It printed:{Environment.NewLine}{Output}");
        }

        // The overload without a timeout also waits for the last of the output
        _process.WaitForExit();
        return _process.ExitCode;
    }

    [UnsupportedOSPlatform("windows")]
    public void Signal(PosixSignal signal) => Assert.That(_process.Signal(signal), Is.True, $"{_name} had already exited");

    public void Kill()
    {
        if (!_process.HasExited)
        {
            _process.Kill();
            _process.WaitForExit();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Kill();
        _process.Dispose();
        _disposed = true;
    }

    private void Add(string? line, bool stdout)
    {
        if (line is null)
        {
            return;
        }

        lock (_lines)
        {
            _lines.Add(line);
            (stdout ? _stdout : _stderr).Add(line);
        }
    }
}
