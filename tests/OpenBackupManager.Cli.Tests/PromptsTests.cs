using Spectre.Console.Testing;

namespace OpenBackupManager.Cli.Tests;

public class PromptsTests
{
    private static readonly string[] Backends = ["OneDrive", "Local folder or NAS", "Dropbox", "Drive [Google]"];

    private TestConsole _console = null!;

    [SetUp]
    public void SetUp() => _console = new TestConsole().Interactive();

    [TearDown]
    public void TearDown() => _console.Dispose();

    [Test]
    public void Choosing_MovesWithTheArrows()
    {
        _console.Input.PushKey(ConsoleKey.DownArrow);
        _console.Input.PushKey(ConsoleKey.DownArrow);
        _console.Input.PushKey(ConsoleKey.Enter);

        Assert.That(Prompts(plain: false).Choose("Where should backups go?", Backends, backend => backend), Is.EqualTo("Dropbox"));
    }

    [Test]
    public void Choosing_SearchesAsItsTyped()
    {
        _console.Input.PushText("goo");
        _console.Input.PushKey(ConsoleKey.Enter);

        Assert.That(Prompts(plain: false).Choose("Where should backups go?", Backends, backend => backend), Is.EqualTo("Drive [Google]"));
    }

    // Read by a screen reader once, as it's never redrawn
    [TestCase("2", "Local folder or NAS")]
    [TestCase("dropbox", "Dropbox")]
    [TestCase("one", "OneDrive")]
    public void Choosing_Plainly_ListsThemNumbered_AndTakesANumberOrName(string answer, string chosen)
    {
        _console.Input.PushTextWithEnter(answer);

        var choice = Prompts(plain: true).Choose("Where should backups go?", Backends, backend => backend);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(choice, Is.EqualTo(chosen));
            Assert.That(_console.Output, Does.Contain("Where should backups go?\n  1. OneDrive\n  2. Local folder or NAS\n  3. Dropbox\n  4. Drive [Google]\n"));
        }
    }

    // Such as the start of more than one, which the plain question asks again
    [Test]
    public void Choosing_Plainly_AsksAgain_ForOneThatIsntThere()
    {
        _console.Input.PushTextWithEnter("d");
        _console.Input.PushTextWithEnter("5");
        _console.Input.PushTextWithEnter("4");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Prompts(plain: true).Choose("Where should backups go?", Backends, backend => backend), Is.EqualTo("Drive [Google]"));
            Assert.That(_console.Output.Split("That isn't one of them").Length, Is.EqualTo(3));
        }
    }

    [Test]
    public void Asking_GivesTheDefault_ForNoAnswer()
    {
        _console.Input.PushKey(ConsoleKey.Enter);

        Assert.That(Prompts(plain: false).Ask("Name [it]?", "Backups"), Is.EqualTo("Backups"));
    }

    // A screen reader would read each * aloud
    [TestCase(false, "******")]
    [TestCase(true, "")]
    public void ASecret_IsntShown(bool plain, string shown)
    {
        _console.Input.PushTextWithEnter("hunter");

        var secret = Prompts(plain).AskSecret("Password?");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secret, Is.EqualTo("hunter"));
            Assert.That(_console.Output, Does.Not.Contain("hunter"));
            Assert.That(_console.Output.TrimEnd(), Is.EqualTo(("Password? " + shown).TrimEnd()));
        }
    }

    [TestCase(null, null, false)]
    [TestCase("1", null, true)]
    [TestCase("yes", null, true)]
    [TestCase("0", null, false)]
    [TestCase("False", null, false)]
    [TestCase("", null, false)]
    [TestCase(null, "dumb", true)]
    [TestCase(null, "xterm-256color", false)]
    public void Plain_WhenAsked_OrTheTerminalCantRedraw(string? asked, string? term, bool plain) =>
        Assert.That(Cli.Prompts.IsPlain(asked, term), Is.EqualTo(plain));

    private Prompts Prompts(bool plain) => new(_console, plain);
}
