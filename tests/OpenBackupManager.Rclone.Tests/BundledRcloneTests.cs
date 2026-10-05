using System.Diagnostics;

namespace OpenBackupManager.Rclone.Tests;

public class BundledRcloneTests
{
    [Test]
    public void FilePath_RunsThePinnedVersion()
    {
        var start = new ProcessStartInfo(BundledRclone.FilePath, "version")
        {
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(start) ?? throw new InvalidOperationException("rclone didn't start");
        var firstLine = process.StandardOutput.ReadLine();
        process.WaitForExit();

        Assert.That(firstLine, Is.EqualTo($"rclone {BundledRclone.Version}"));
    }
}
