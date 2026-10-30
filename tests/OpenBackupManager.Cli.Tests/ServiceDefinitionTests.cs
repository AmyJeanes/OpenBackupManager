using System.Xml.Linq;

namespace OpenBackupManager.Cli.Tests;

// What obm service install writes, checked on every OS
public class ServiceDefinitionTests
{
    private const string Obmd = "/opt/obm/obmd";

    private static readonly Dictionary<string, string> Shell = new()
    {
        ["HOME"] = "/home/amy",
        ["XDG_CONFIG_HOME"] = "/home/amy/dotfiles",
        ["XDG_STATE_HOME"] = "/home/amy/state",
    };

    // systemd's own, with none of the shell's XDG folders
    private static readonly Dictionary<string, string> Systemd = new() { ["HOME"] = "/home/amy" };

    [Test]
    public void Systemd_StartsTheEngine_WithTheShellsFolders()
    {
        var service = new SystemdService(null, Obmd, Shell.GetValueOrDefault, Systemd.GetValueOrDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(service.Unit, Is.EqualTo("openbackupmanager.service"));
            Assert.That(service.File, Is.EqualTo(Path.Combine("/home/amy", ".config", "systemd", "user", "openbackupmanager.service")));
            Assert.That(Lines(service.Definition()), Does.Contain("ExecStart=\"/opt/obm/obmd\" \"--started-by\" \"service\""));
            Assert.That(Lines(service.Definition()), Does.Contain("Environment=\"XDG_CONFIG_HOME=/home/amy/dotfiles\""));
            Assert.That(Lines(service.Definition()), Does.Contain("Environment=\"XDG_STATE_HOME=/home/amy/state\""));
            Assert.That(service.Definition(), Does.Not.Contain("XDG_CACHE_HOME"));
            Assert.That(Lines(service.Definition()), Does.Contain("Restart=on-failure"));
            Assert.That(Lines(service.Definition()), Does.Contain("WantedBy=default.target"));
        }
    }

    // systemd would read % as a specifier and $ as a variable
    [Test]
    public void Systemd_QuotesAHome()
    {
        var service = new SystemdService(@"/home/amy/100% ""backups"" $HOME\x", Obmd, Shell.GetValueOrDefault, Systemd.GetValueOrDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Lines(service.Definition()),
                Does.Contain(@"ExecStart=""/opt/obm/obmd"" ""--started-by"" ""service"" ""--home"" ""/home/amy/100%% \""backups\"" $$HOME\\x"""));
            Assert.That(service.Definition(), Does.Not.Contain("Environment="));
            Assert.That(service.Unit, Does.Match("^openbackupmanager-[0-9a-f]{16}\\.service$"));
        }
    }

    // Where systemd will find it, whatever the shell says
    [Test]
    public void Systemd_PutsTheUnit_InSystemdsOwnConfigFolder()
    {
        var systemd = new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "/home/amy/systemd-config" };
        var service = new SystemdService(null, Obmd, Shell.GetValueOrDefault, systemd.GetValueOrDefault);

        Assert.That(service.File, Is.EqualTo(Path.Combine("/home/amy/systemd-config", "systemd", "user", "openbackupmanager.service")));
    }

    [Test]
    public void Systemd_ReadsItsOwnEnvironment()
    {
        var variables = SystemdService.ParseEnvironment("HOME=/home/amy\nLANG=en_GB.UTF-8\nXDG_CONFIG_HOME=/home/amy/a=b\n\n");

        Assert.That(variables, Is.EqualTo(new Dictionary<string, string>
        {
            ["HOME"] = "/home/amy",
            ["LANG"] = "en_GB.UTF-8",
            ["XDG_CONFIG_HOME"] = "/home/amy/a=b",
        }));
    }

    [Test]
    public void LaunchAgent_StartsTheEngine_WithTheShellsConfigFolder()
    {
        var agent = new LaunchAgent(null, Obmd, Shell.GetValueOrDefault);
        var plist = XDocument.Parse(agent.Definition());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agent.Label, Is.EqualTo("io.github.amyjeanes.openbackupmanager.engine"));
            Assert.That(agent.File, Is.EqualTo(Path.Combine("/home/amy", "Library", "LaunchAgents", "io.github.amyjeanes.openbackupmanager.engine.plist")));
            Assert.That(plist.DocumentType?.PublicId, Is.EqualTo("-//Apple//DTD PLIST 1.0//EN"));
            Assert.That(Value(plist, "Label").Value, Is.EqualTo(agent.Label));
            Assert.That(Value(plist, "ProgramArguments").Elements().Select(argument => argument.Value), Is.EqualTo([Obmd, "--started-by", "service"]));
            Assert.That(Entries(Value(plist, "EnvironmentVariables")), Is.EqualTo(new Dictionary<string, string> { ["XDG_CONFIG_HOME"] = "/home/amy/dotfiles" }));
            Assert.That(Value(plist, "RunAtLoad").Name.LocalName, Is.EqualTo("true"));
            Assert.That(Entries(Value(plist, "KeepAlive")), Is.EqualTo(new Dictionary<string, string> { ["SuccessfulExit"] = "false" }));
        }
    }

    [Test]
    public void LaunchAgent_PassesAHome()
    {
        var agent = new LaunchAgent("/Users/amy/<backups> & more/", Obmd, Shell.GetValueOrDefault);
        var plist = XDocument.Parse(agent.Definition());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Value(plist, "ProgramArguments").Elements().Select(argument => argument.Value),
                Is.EqualTo([Obmd, "--started-by", "service", "--home", "/Users/amy/<backups> & more"]));
            Assert.That(plist.Descendants("key").Select(key => key.Value), Does.Not.Contain("EnvironmentVariables"));
            Assert.That(agent.Label, Does.Match("^io\\.github\\.amyjeanes\\.openbackupmanager\\.engine\\.[0-9a-f]{16}$"));
        }
    }

    // So each home is found again from the same --home, however it's written
    [Test]
    public void EachHome_HasItsOwn()
    {
        static string Unit(string? home) => new SystemdService(home, Obmd, Shell.GetValueOrDefault, Systemd.GetValueOrDefault).Unit;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unit("/srv/a"), Is.EqualTo(Unit("/srv/a/")));
            Assert.That(Unit("/srv/a"), Is.Not.EqualTo(Unit("/srv/b")));
            Assert.That(Unit("/srv/a"), Is.Not.EqualTo(Unit("/srv/A")));
            Assert.That(Unit("/srv/a"), Is.Not.EqualTo(Unit(null)));
        }
    }

    private static string[] Lines(string definition) => definition.Split('\n');

    // The element after a key in a plist dict
    private static XElement Value(XDocument plist, string key) =>
        plist.Root!.Element("dict")!.Elements("key").Single(element => element.Value == key).ElementsAfterSelf().First();

    // With <true/> and <false/> as true and false
    private static Dictionary<string, string> Entries(XElement dict) =>
        dict.Elements("key").ToDictionary(key => key.Value, key => key.ElementsAfterSelf().First() is { IsEmpty: true } flag ? flag.Name.LocalName : key.ElementsAfterSelf().First().Value);
}
