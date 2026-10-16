using System.Runtime.Versioning;

namespace OpenBackupManager.Core.Tests;

public class RunningAsTests
{
    private const uint Me = 1000;
    private const string MyName = "amy";

    [Test]
    public void SwitchedFromAnotherUser_NothingSet_IsNot() =>
        Assert.That(Switched([]), Is.False);

    [TestCase("SUDO_UID", "1000")]
    [TestCase("PKEXEC_UID", "1000")]
    [TestCase("DOAS_USER", MyName)]
    public void SwitchedFromAnotherUser_FromThemselves_IsNot(string variable, string value) =>
        Assert.That(Switched(new() { [variable] = value }), Is.False);

    // run0 sets SUDO_UID too
    [TestCase("SUDO_UID", "1001")]
    [TestCase("PKEXEC_UID", "1001")]
    [TestCase("DOAS_USER", "someone")]
    public void SwitchedFromAnotherUser_FromSomeoneElse_Is(string variable, string value) =>
        Assert.That(Switched(new() { [variable] = value }), Is.True);

    [Test]
    public void SwitchedFromAnotherUser_AnyOneOfThemFromSomeoneElse_Is() =>
        Assert.That(Switched(new() { ["SUDO_UID"] = "1000", ["PKEXEC_UID"] = "0" }), Is.True);

    [Test]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void AdministratorWithNormalTokenToo_Answers() =>
        Assert.That(RunningAs.AdministratorWithNormalTokenToo, Throws.Nothing);

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void SwitchedFromAnotherUser_ForThisProcess_Answers() =>
        Assert.That(RunningAs.SwitchedFromAnotherUser, Throws.Nothing);

    private static bool Switched(Dictionary<string, string> environment) =>
        RunningAs.SwitchedFromAnotherUser(name => environment.GetValueOrDefault(name), Me, MyName);
}
