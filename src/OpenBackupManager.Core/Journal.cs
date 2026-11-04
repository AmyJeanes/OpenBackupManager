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

        var start = new ProcessStartInfo("journalctl")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in (string[])["--user", "_PID=" + run.ProcessId.ToString(CultureInfo.InvariantCulture), "_BOOT_ID=" + run.Boot.Replace("-", "", StringComparison.Ordinal), "--output=cat", "--no-pager"])
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(Timeout))
            {
                process.Kill();
                return null;
            }

            return CrashReports.Ending(output.Result);
        }
        catch (Win32Exception)
        {
            // No journalctl, as without systemd
            return null;
        }
    }
}
