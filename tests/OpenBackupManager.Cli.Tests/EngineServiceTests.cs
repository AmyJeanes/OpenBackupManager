namespace OpenBackupManager.Cli.Tests;

public class EngineServiceTests
{
    // Waiting for an engine to stop runs launchctl many times over, and macOS allows 256 open files
    [Test]
    [Platform(Exclude = "Win")]
    public void TryRun_LeavesNothingOpen()
    {
        var open = OpenFiles();
        for (var i = 0; i < 100; i++)
        {
            Runner.Run("true");
        }

        Assert.That(OpenFiles(), Is.LessThan(open + 20));
    }

    private static int OpenFiles() => Directory.GetFileSystemEntries("/dev/fd").Length;

    // Only to reach TryRun
    private abstract class Runner() : EngineService(null, "", "", _ => null)
    {
        public static void Run(string program) => TryRun(program);
    }
}
