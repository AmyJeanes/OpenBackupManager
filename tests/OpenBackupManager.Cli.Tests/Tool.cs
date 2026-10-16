using System.Diagnostics;

namespace OpenBackupManager.Cli.Tests;

// The built obm or obmd, run as a user would, with what it prints collected
internal sealed class Tool : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string _name;
    private readonly Process _process;
    private readonly List<string> _lines = [];

    private Tool(string name, Process process)
    {
        _name = name;
        _process = process;
    }

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

    public static Tool Start(string name, params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? name + ".exe" : name), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("OBM_HOME");

        var tool = new Tool(name, new Process { StartInfo = start });
        tool._process.OutputDataReceived += (_, e) => tool.Add(e.Data);
        tool._process.ErrorDataReceived += (_, e) => tool.Add(e.Data);
        tool._process.Start();
        tool._process.BeginOutputReadLine();
        tool._process.BeginErrorReadLine();
        return tool;
    }

    // Runs it to the end
    public static (int ExitCode, string Output) Run(string name, params string[] args)
    {
        using var tool = Start(name, args);
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
            }

            Thread.Sleep(100);
        }
    }

    public int WaitForExit()
    {
        if (!_process.WaitForExit(Timeout))
        {
            Assert.Fail($"{_name} didn't exit. It printed:{Environment.NewLine}{Output}");
        }

        // The overload without a timeout also waits for the last of the output
        _process.WaitForExit();
        return _process.ExitCode;
    }

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
        Kill();
        _process.Dispose();
    }

    private void Add(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_lines)
        {
            _lines.Add(line);
        }
    }
}
