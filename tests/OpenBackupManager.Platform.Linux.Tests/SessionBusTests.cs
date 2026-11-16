using System.Net.Sockets;
using System.Runtime.Versioning;

namespace OpenBackupManager.Platform.Linux.Tests;

// Where Secret Service is reached, as over SSH or with su, where DBUS_SESSION_BUS_ADDRESS may not be set
[Platform(Include = "Linux")]
[SupportedOSPlatform("linux")]
public class SessionBusTests
{
    private string _root = "";
    private string _folder = "";
    private Socket? _bus;

    [SetUp]
    public void SetUp()
    {
        _bus = null;
        _root = _folder = Directory.CreateTempSubdirectory("obm-tests-").FullName;
    }

    [TearDown]
    public void TearDown()
    {
        _bus?.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void TheVariable_IsUsed() =>
        Assert.That(SecretService.SessionBus(Environment(("DBUS_SESSION_BUS_ADDRESS", "unix:path=/elsewhere"), ("XDG_RUNTIME_DIR", Bus())), 1000),
            Is.EqualTo("unix:path=/elsewhere"));

    [Test]
    public void WithoutTheVariable_TheBusInXdgRuntimeDir_IsUsed() =>
        Assert.That(SecretService.SessionBus(Environment(("XDG_RUNTIME_DIR", Bus())), 1000), Is.EqualTo($"unix:path={_folder}/bus"));

    [Test]
    public void AnEmptyVariable_CountsAsNotSet() =>
        Assert.That(SecretService.SessionBus(Environment(("DBUS_SESSION_BUS_ADDRESS", ""), ("XDG_RUNTIME_DIR", Bus())), 1000), Is.EqualTo($"unix:path={_folder}/bus"));

    [Test]
    public void ABusInAFolderWithSpaces_IsEscaped()
    {
        _folder = Path.Combine(_folder, "with spaces");
        Directory.CreateDirectory(_folder);

        Assert.That(SecretService.SessionBus(Environment(("XDG_RUNTIME_DIR", Bus())), 1000), Is.EqualTo($"unix:path={_folder.Replace(" ", "%20", StringComparison.Ordinal)}/bus"));
    }

    [Test]
    public void NoBus_IsNull() =>
        Assert.That(SecretService.SessionBus(Environment(("XDG_RUNTIME_DIR", _folder)), 1000), Is.Null);

    // A socket, as dbus-daemon makes. Kept open, as .NET removes its file when it's closed
    private string Bus()
    {
        _bus = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _bus.Bind(new UnixDomainSocketEndPoint(Path.Combine(_folder, "bus")));
        return _folder;
    }

    private static Func<string, string?> Environment(params (string Name, string Value)[] variables) =>
        name => variables.FirstOrDefault(v => v.Name == name).Value;
}
