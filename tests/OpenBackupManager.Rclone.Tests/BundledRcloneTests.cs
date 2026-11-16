using System.Diagnostics;

namespace OpenBackupManager.Rclone.Tests;

public class BundledRcloneTests
{
    [Test]
    public void FilePath_RunsThePinnedVersion()
    {
        // A config of its own, as without one rclone makes its config folder in the real home
        var folder = Directory.CreateTempSubdirectory("obm-rclone-").FullName;
        try
        {
            var start = new ProcessStartInfo(BundledRclone.FilePath, ["version", "--config", Path.Combine(folder, "rclone.conf")])
            {
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(start) ?? throw new InvalidOperationException("rclone didn't start");
            var firstLine = process.StandardOutput.ReadLine();
            process.WaitForExit();

            Assert.That(firstLine, Is.EqualTo($"rclone {BundledRclone.Version}"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
