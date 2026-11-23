using System.IO.Pipes;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Core.Tests;

public class EngineClientTests
{
    private string _home = "";

    [SetUp]
    public void SetUp() => _home = Directory.CreateTempSubdirectory("obm-client-").FullName;

    [TearDown]
    public void DeleteHome() => Directory.Delete(_home, recursive: true);

    [Test]
    public async Task ConnectAsync_SaysTheEngineDidntAnswer_WhenItAcceptsTheConnectionButStaysQuiet()
    {
        var paths = AppPaths.InHome(_home);
        Directory.CreateDirectory(RunFolder.Path(paths));
        await using var engine = new NamedPipeServerStream(
            EngineSocket.Name(paths), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var accepted = engine.WaitForConnectionAsync();
        using var giveUp = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        Assert.That(
            async () => await EngineClient.ConnectAsync(paths, giveUp.Token),
            Throws.TypeOf<EngineException>().With.Message.Contains("accepted the connection but didn't answer"));
        Assert.That(giveUp.IsCancellationRequested, Is.False);
        await accepted;
    }
}
