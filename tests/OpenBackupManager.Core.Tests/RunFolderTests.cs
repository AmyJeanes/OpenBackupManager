using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Core.Tests;

public class RunFolderTests
{
    private static readonly AppPaths Home = AppPaths.InHome(Path.Combine(Path.GetTempPath(), "obm"));

    [Test]
    public void Socket_IsInTheRunFolder() =>
        Assert.That(RunFolder.Socket(Home, "engine.sock"), Is.EqualTo(Path.Combine(Home.State, "run", "engine.sock")));

    // Longer than the engine checks it has room for
    [Test]
    public void Socket_WithALongerName_IsRefused() =>
        Assert.That(() => RunFolder.Socket(Home, new string('x', 17)), Throws.TypeOf<ArgumentOutOfRangeException>());

    // Whether or not something made it first, with what it would have inherited
    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void Create_OnWindows_LetsOnlyThisUserIn([Values] bool madeFirst)
    {
        var home = AppPaths.InHome(Directory.CreateTempSubdirectory("obm-run-").FullName);
        try
        {
            if (madeFirst)
            {
                Directory.CreateDirectory(RunFolder.Path(home));
            }

            RunFolder.Create(home);

            var security = new DirectoryInfo(RunFolder.Path(home)).GetAccessControl();
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(security.AreAccessRulesProtected, Is.True, "Inherits nothing");
                Assert.That(rules.Select(rule => rule.IdentityReference), Is.EqualTo([WindowsIdentity.GetCurrent().User]));
                Assert.That(rules.Single().FileSystemRights, Is.EqualTo(FileSystemRights.FullControl));
            }
        }
        finally
        {
            Directory.Delete(home.State, recursive: true);
        }
    }
}
