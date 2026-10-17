namespace OpenBackupManager.App.Windows.Tests;

public class StartAgainWithoutAdminTests
{
    private const string Home = @"C:\Some home";

    [Test]
    public void StartsThisExe_ForTheSameHome_MarkedAsStartedAgain()
    {
        var started = new List<(string File, string Arguments)>();

        var startedAgain = Program.StartAgainWithoutAdmin(["--home", Home], Home, (file, arguments) => Start(started, file, arguments));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(startedAgain, Is.True);
            Assert.That(started, Is.EqualTo([(Environment.ProcessPath, $"--home \"{Home}\" --restarted")]));
        }
    }

    [Test]
    public void AtLogin_StaysThatWay()
    {
        var started = new List<(string File, string Arguments)>();

        Program.StartAgainWithoutAdmin(["--at-login"], Home, (file, arguments) => Start(started, file, arguments));

        Assert.That(started.Single().Arguments, Is.EqualTo($"--home \"{Home}\" --restarted --at-login"));
    }

    [Test]
    public void ACopyStartedAgain_DoesntStartAnother()
    {
        var started = new List<(string File, string Arguments)>();

        var startedAgain = Program.StartAgainWithoutAdmin(["--restarted"], Home, (file, arguments) => Start(started, file, arguments));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(startedAgain, Is.False);
            Assert.That(started, Is.Empty);
        }
    }

    [Test]
    public void WhenItCantStart_SaysSo() => Assert.That(Program.StartAgainWithoutAdmin([], Home, (_, _) => false), Is.False);

    private static bool Start(List<(string File, string Arguments)> started, string file, string arguments)
    {
        started.Add((file, arguments));
        return true;
    }
}
