using System.Diagnostics;

namespace OpenBackupManager.Rclone.Tests;

public class RemoteSetupTests
{
    private const string Password = "correct horse battery staple";

    private static readonly Dictionary<string, string> NoParameters = [];

    private string _folder = null!;

    [SetUp]
    public void SetUp() => _folder = Directory.CreateTempSubdirectory("obm-setup-").FullName;

    [TearDown]
    public void TearDown() => TestFolders.Delete(_folder);

    [Test]
    public async Task AsksRclonesQuestions_AndSavesTheAnswers()
    {
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);
        var setup = new RemoteSetup(rcd.Client, "remote", NoParameters);

        var question = await setup.StartAsync("alias", CancellationToken.None);
        var done = await setup.AnswerAsync(Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Is.Not.Null);
            Assert.That(question!.Name, Is.EqualTo("remote"));
            Assert.That(question.Required, Is.True);
            Assert.That(question.Error, Is.Null);
            // Not asked whether to edit the advanced options, which alias has
            Assert.That(done, Is.Null);
            Assert.That(Config(), Does.Contain("[remote]").And.Contain("type = alias").And.Contain("remote = " + Target));
        }
    }

    [Test]
    public async Task AParameter_AnswersItsQuestion()
    {
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);
        var setup = new RemoteSetup(rcd.Client, "remote", new Dictionary<string, string> { ["remote"] = Target });

        var question = await setup.StartAsync("alias", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Is.Null);
            Assert.That(Config(), Does.Contain("remote = " + Target));
        }
    }

    [Test]
    public async Task APassword_IsObscured()
    {
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);
        var setup = new RemoteSetup(rcd.Client, "remote", new Dictionary<string, string> { ["remote"] = Target });
        List<SetupQuestion> asked = [];

        var question = await setup.StartAsync("crypt", CancellationToken.None);
        while (question is not null)
        {
            asked.Add(question);
            question = await setup.AnswerAsync(question.Name == "password" ? Password : "", CancellationToken.None);
        }

        var saved = Config().Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("password", StringComparison.Ordinal)).ToList();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(asked.Where(q => q.IsPassword).Select(q => q.Name), Is.EqualTo(["password", "password2"]));
            // Only the one answered, as an empty one stays empty
            Assert.That(saved, Has.Count.EqualTo(1));
            Assert.That(saved[0], Does.StartWith("password = ").And.Not.Contain(Password));
            // Once, as rclone takes it, not twice
            Assert.That(await RevealAsync(saved[0]["password = ".Length..]), Is.EqualTo(Password));
        }
    }

    [Test]
    public async Task APasswordGivenAsAParameter_IsObscured()
    {
        // Long enough and all base64, so it looks to rclone as if it's already obscured
        var password = "correcthorsebatterystaple42";
        await using var rcd = await Rcd.StartAsync(Options(), CancellationToken.None);
        var setup = new RemoteSetup(rcd.Client, "remote", new Dictionary<string, string> { ["remote"] = Target, ["password"] = password });

        var question = await setup.StartAsync("crypt", CancellationToken.None);
        while (question is not null)
        {
            question = await setup.AnswerAsync("", CancellationToken.None);
        }

        var saved = Config().Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith("password = ", StringComparison.Ordinal));
        Assert.That(await RevealAsync(saved["password = ".Length..]), Is.EqualTo(password));
    }

    [Test]
    public async Task WithItsConfigUnlocked_ItsSavedEncrypted()
    {
        var options = Options();
        await using var rcd = await Rcd.StartAsync(options, CancellationToken.None);
        await rcd.Client.UnlockConfigAsync(Password, CancellationToken.None);
        var setup = new RemoteSetup(rcd.Client, "remote", new Dictionary<string, string> { ["remote"] = Target });

        await setup.StartAsync("alias", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(RcloneConfigFile.IsEncrypted(options.Config), Is.True);
            Assert.That(Config(), Does.Not.Contain(Target));
        }
    }

    [Test]
    public async Task AStepWithNoQuestion_GoesOn_AndItsErrorComesWithTheNext()
    {
        await using var rc = FakeHttp.OnSocket(
            Socket,
            """{"State": "check", "Result": "found", "Error": "No drives found\n"}""",
            """{"State": "choose", "Option": {"Name": "drive", "Help": "Which drive?", "Default": "a", "DefaultStr": "a", "Examples": [{"Value": "a", "Help": "A"}], "Exclusive": true, "Type": "string"}}""");
        using var client = new RcClient(Socket);
        var setup = new RemoteSetup(client, "remote", NoParameters);

        var question = await setup.StartAsync("onedrive", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(question, Is.EqualTo(new SetupQuestion("drive", "Which drive?", "a", question!.Choices, false, false, true, "string", "No drives found")));
            Assert.That(question.Choices, Is.EqualTo([new SetupChoice("a", "A")]));
            Assert.That(rc.Calls[1].Body, Does.Contain("\"state\":\"check\"").And.Contain("\"result\":\"found\""));
        }
    }

    [Test]
    public async Task AnErrorFollowedByAnother_StopsIt()
    {
        await using var rc = FakeHttp.OnSocket(
            Socket,
            """{"State": "token", "Error": "Couldn't decode response"}""",
            """{"State": "token", "Error": "Couldn't decode response"}""");
        using var client = new RcClient(Socket);
        var setup = new RemoteSetup(client, "remote", NoParameters);

        Assert.That(() => setup.StartAsync("onedrive", CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.EqualTo("rclone couldn't set remote up: Couldn't decode response"));
    }

    [Test]
    public async Task AnErrorAsItEnds_StopsIt()
    {
        await using var rc = FakeHttp.OnSocket(Socket, """{"State": "", "Error": "No drives found"}""");
        using var client = new RcClient(Socket);
        var setup = new RemoteSetup(client, "remote", NoParameters);

        Assert.That(() => setup.StartAsync("onedrive", CancellationToken.None),
            Throws.TypeOf<RcloneException>().With.Message.EqualTo("rclone couldn't set remote up: No drives found"));
    }

    [Test]
    public async Task EachStep_PassesTheParametersAgain()
    {
        await using var rc = FakeHttp.OnSocket(
            Socket,
            """{"State": "next", "Option": {"Name": "region", "Default": null, "DefaultStr": "<nil>"}}""",
            """{"State": ""}""");
        using var client = new RcClient(Socket);
        var setup = new RemoteSetup(client, "remote", new Dictionary<string, string> { ["config_is_local"] = "false" });

        var question = await setup.StartAsync("onedrive", CancellationToken.None);
        await setup.AnswerAsync("global", CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            // As none was given
            Assert.That(question!.Default, Is.Empty);
            Assert.That(rc.Calls.Select(call => call.Path), Is.EqualTo(["config/create", "config/update"]));
            Assert.That(rc.Calls, Has.All.Matches<(string, string Body)>(call =>
                call.Body.Contains("\"config_is_local\":\"false\"", StringComparison.Ordinal) && call.Body.Contains("\"config_fs_advanced\":\"false\"", StringComparison.Ordinal)));
        }
    }

    private string Target => Path.Combine(_folder, "files");

    private string Socket => Path.Combine(_folder, "fake.sock");

    private string Config() => File.ReadAllText(Path.Combine(_folder, "rclone.conf"));

    // With rclone's own reveal, and no config of the user's. After --, as one in 64 starts with -, which rclone would
    // take as a flag. Saying how it ended otherwise
    private static async Task<string> RevealAsync(string obscured)
    {
        using var reveal = Process.Start(new ProcessStartInfo(BundledRclone.FilePath, ["reveal", "--config", "", "--", obscured])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var output = reveal.StandardOutput.ReadToEndAsync();
        var errors = reveal.StandardError.ReadToEndAsync();
        await reveal.WaitForExitAsync();
        var clear = (await output).TrimEnd('\r', '\n');
        if (reveal.ExitCode != 0 || clear.Length == 0)
        {
            Assert.Fail($"rclone reveal printed \"{clear}\" and exited with {reveal.ExitCode}, saying: {await errors}");
        }

        return clear;
    }

    private RcdOptions Options() => new(
        BundledRclone.FilePath,
        Path.Combine(_folder, "rclone.sock"),
        Path.Combine(_folder, "rclone.conf"),
        Path.Combine(_folder, "cache"),
        Path.Combine(_folder, "rclone.log"),
        "OpenBackupManager-Tests");
}
