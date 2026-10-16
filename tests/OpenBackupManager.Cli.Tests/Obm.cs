using System.Diagnostics;

namespace OpenBackupManager.Cli.Tests;

// The built obm, run as a user would, with what it prints collected
internal sealed class Obm : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly List<string> _lines = [];

    private Obm(Process process) => _process = process;

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

    public static Obm Start(params string[] args)
    {
        var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "obm.exe" : "obm"), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment.Remove("OBM_HOME");

        var obm = new Obm(new Process { StartInfo = start });
        obm._process.OutputDataReceived += (_, e) => obm.Add(e.Data);
        obm._process.ErrorDataReceived += (_, e) => obm.Add(e.Data);
        obm._process.Start();
        obm._process.BeginOutputReadLine();
        obm._process.BeginErrorReadLine();
        return obm;
    }

    // Runs it to the end
    public static (int ExitCode, string Output) Run(params string[] args)
    {
        using var obm = Start(args);
        return (obm.WaitForExit(), obm.Output);
    }

    public void WaitFor(string text)
    {
        var until = DateTime.UtcNow + Timeout;
        while (!Output.Contains(text, StringComparison.Ordinal))
        {
            if (_process.HasExited || DateTime.UtcNow > until)
            {
                Assert.Fail($"obm didn't print \"{text}\". It printed:{Environment.NewLine}{Output}");
            }

            Thread.Sleep(100);
        }
    }

    public int WaitForExit()
    {
        if (!_process.WaitForExit(Timeout))
        {
            Assert.Fail($"obm didn't exit. It printed:{Environment.NewLine}{Output}");
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
