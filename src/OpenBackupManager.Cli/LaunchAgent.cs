using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace OpenBackupManager.Cli;

// A LaunchAgent on macOS, which launchd runs in the user's session from when they sign in
internal sealed class LaunchAgent(string? home, string obmd, Func<string, string?> environment) : EngineService(home, obmd, environment)
{
    public string Label => HomeKey is null ? "io.github.amyjeanes.openbackupmanager.engine" : $"io.github.amyjeanes.openbackupmanager.engine.{HomeKey}";

    public override string File => Path.Combine(Environment("HOME")!, "Library", "LaunchAgents", Label + ".plist");

    public override string HowToCheck => $"launchctl print {Domain}/{Label}";

    // The user's own session, where agents run while they're signed in
    private static string Domain => "gui/" + NativeMethods.getuid().ToString(CultureInfo.InvariantCulture);

    public override void Install()
    {
        // Fails when it isn't loaded, which is fine. Otherwise it waits for the engine to stop
        TryRun("launchctl", "bootout", $"{Domain}/{Label}");
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllText(File, Definition());
        Run("launchctl", "bootstrap", Domain, File);
    }

    public override void Uninstall()
    {
        TryRun("launchctl", "bootout", $"{Domain}/{Label}");
        System.IO.File.Delete(File);
    }

    // Started again if it crashes or fails, as KeepAlive does when the engine doesn't exit cleanly, but not once it's
    // stopped on purpose
    internal string Definition()
    {
        var variables = Variables("XDG_CONFIG_HOME").ToList();
        var plist = new XElement(
            "plist",
            new XAttribute("version", "1.0"),
            new XElement(
                "dict",
                Key("Label"),
                new XElement("string", Label),
                Key("ProgramArguments"),
                new XElement("array", Arguments.Select(argument => new XElement("string", argument))),
                variables.Count == 0 ? null : Key("EnvironmentVariables"),
                variables.Count == 0 ? null : new XElement("dict", variables.SelectMany(variable => new[] { Key(variable.Name), new XElement("string", variable.Value) })),
                Key("RunAtLoad"),
                new XElement("true"),
                Key("KeepAlive"),
                new XElement("dict", Key("SuccessfulExit"), new XElement("false"))));
        var document = new XDocument(
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XComment(" Written by obm service install, and removed by obm service uninstall "),
            plist);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + document.ToString().ReplaceLineEndings("\n") + "\n";
    }

    private static XElement Key(string name) => new("key", name);

    private static class NativeMethods
    {
        [DllImport("libc")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint getuid();
    }
}
