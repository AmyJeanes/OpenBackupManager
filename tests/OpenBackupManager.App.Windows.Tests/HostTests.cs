using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core;

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

        Assert.That(ReadLog(), Does.Contain("[INF] OpenBackupManager.App.Windows.Tests.HostTests: Test message"));
    }

    [Test]
    public void Logs_HideSecrets()
    {
        using (var host = Program.BuildHost(_home))
        {
            var logger = host.Services.GetRequiredService<ILogger<HostTests>>();
            var secret = new Secret("token-value");
            LogSecret(logger, secret, secret);
        }

        Assert.That(ReadLog(), Does.Contain("Secret ***").And.Not.Contain("token-value"));
    }

    private string ReadLog() => File.ReadAllText(Directory.GetFiles(Path.Combine(_home, "logs"), $"app-{DateTime.Now:yyyyMMdd}.log").Single());

    [LoggerMessage(Level = LogLevel.Information, Message = "Test message")]
    private static partial void LogTest(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Secret {Secret}, destructured {@Destructured}")]
    private static partial void LogSecret(ILogger logger, Secret secret, Secret destructured);
}
