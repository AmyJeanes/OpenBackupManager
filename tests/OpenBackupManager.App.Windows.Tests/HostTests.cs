using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace OpenBackupManager.App.Windows.Tests;

public sealed partial class HostTests
{
    private string _home = "";

    [SetUp]
    public void CreateHome() => _home = Directory.CreateTempSubdirectory("obm-tests-").FullName;

    [TearDown]
    public void DeleteHome() => Directory.Delete(_home, recursive: true);

    // Building checks each service's dependencies without creating any, since they need the UI thread
    [Test]
    public void Build_FindsEveryServicesDependencies() => Assert.DoesNotThrow(() => Program.BuildHost(_home).Dispose());

    [Test]
    public void Logs_AreWrittenToTheHomesLogsFolder()
    {
        using (var host = Program.BuildHost(_home))
        {
            var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
            LogTest(logger);
        }

        var log = Directory.GetFiles(Path.Combine(_home, "logs"), $"app-{DateTime.Now:yyyyMMdd}.log").Single();
        Assert.That(File.ReadAllText(log), Does.Contain("[INF] OpenBackupManager.App.Windows.Tests.HostTests: Test message"));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Test message")]
    private static partial void LogTest(ILogger logger);
}
