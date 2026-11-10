using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace OpenBackupManager.Core;

// What the engine printed as it ended, from the user's journal, where systemd sends a service's stderr. A FailFast's
// message and stack only go there. Only kept when the journal is kept on disk, as it is by default
internal static class Journal
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public static string? Printed(Run run)
    {
        if (run.Boot is null)
        {
            return null;
        }

        var printed = Run(Timeout, "--user", "_PID=" + run.ProcessId.ToString(CultureInfo.InvariantCulture), "_BOOT_ID=" + run.Boot.Replace("-", "", StringComparison.Ordinal), "--output=cat", "--no-pager");
        return printed is null ? null : CrashReports.Ending(printed.Value.Output);
    }

    // Null if it isn't there, as without systemd, or doesn't finish in time
    internal static (string Output, string Errors)? Run(TimeSpan timeout, params string[] arguments)
    {
        var start = new ProcessStartInfo(SystemPrograms.Find("journalctl"), arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        try
        {
            using var process = Process.Start(start)!;
            using var stdout = process.StandardOutput;
            using var stderr = process.StandardError;
            var errors = stderr.ReadToEndAsync();
            var output = stdout.ReadToEndAsync();
            if (!process.WaitForExit(timeout))
            {
                process.Kill();
                return null;
            }

            return (output.Result, errors.Result);
        }
        catch (Win32Exception)
        {
            return null;
        }
    }
}
