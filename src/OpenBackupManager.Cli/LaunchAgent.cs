using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using OpenBackupManager.Core;

namespace OpenBackupManager.Cli;

// A LaunchAgent on macOS, which launchd runs in the user's session from when they sign in
internal sealed class LaunchAgent(string? home, string obmd, string obm, Func<string, string?> environment)
    : EngineService(home, obmd, obm, environment)
{
    // Longer than launchd gives a job to exit before it kills it, 20 seconds unless ExitTimeOut says otherwise
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    public string Label => HomeKey is null ? "io.github.amyjeanes.openbackupmanager.engine" : $"io.github.amyjeanes.openbackupmanager.engine.{HomeKey}";

    public override string File => Path.Combine(Environment("HOME")!, "Library", "LaunchAgents", Label + ".plist");

    public override string HowToCheck => $"launchctl print {Target}";

    // launchd then won't start it at sign-in, nor say why
    public override bool SwitchedOff => IsInstalled && ServiceManagement.Status(File) == ServiceManagement.RequiresApproval;

    public override string WhereToSwitchOn => "System Settings > General > Login Items & Extensions";

    // The user's own session, where agents run while they're signed in
    private static string Domain => "gui/" + NativeMethods.getuid().ToString(CultureInfo.InvariantCulture);

    private string Target => $"{Domain}/{Label}";

    // launchd sends stderr nowhere unless told, and it's where a FailFast's message, or why it couldn't start, goes
    private string ErrorOutput => CrashReports.ErrorOutput(Home is null ? AppPaths.ForOS(OSPlatform.OSX, Environment) : AppPaths.InHome(Home));

    public override void Install()
    {
        BootOut();
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        // launchd won't make the folder
        Directory.CreateDirectory(Path.GetDirectoryName(ErrorOutput)!);
        System.IO.File.WriteAllText(File, Definition());
        Run("launchctl", "bootstrap", Domain, File);
    }

    public override void Uninstall()
    {
        BootOut();
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
                Key("StandardErrorPath"),
                new XElement("string", ErrorOutput),
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

    // Stops the engine and unloads the agent, if it's loaded. bootout returns once it's sent SIGTERM, but launchd keeps
    // the job until the engine exits. Until then, a client can still reach the engine, and bootstrap fails with error 5
    private void BootOut()
    {
        TryRun("launchctl", "bootout", Target);
        var until = DateTime.UtcNow + StopTimeout;
        while (TryRun("launchctl", "print", Target).ExitCode == 0)
        {
            if (DateTime.UtcNow > until)
            {
                throw new ServiceException(string.Create(CultureInfo.InvariantCulture, $"the engine didn't stop within {StopTimeout.TotalSeconds} seconds. {HowToCheck} might say why"));
            }

            Thread.Sleep(100);
        }
    }

    private static class NativeMethods
    {
        [DllImport("libc")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        public static extern uint getuid();
    }

    // SMAppService.statusForLegacyURL, from macOS 13, which says whether Login Items lets any agent's plist run. Reached
    // through the Objective-C runtime, as .NET has no binding for it
    private static class ServiceManagement
    {
        // SMAppServiceStatusRequiresApproval, which is what switching it off in Login Items gives
        public const nint RequiresApproval = 2;

        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        public static nint Status(string plist)
        {
            if (!OperatingSystem.IsMacOSVersionAtLeast(13))
            {
                return 0;
            }

            NativeLibrary.Load("/System/Library/Frameworks/ServiceManagement.framework/ServiceManagement");
            var path = WithText(plist, text => Call("NSString", "stringWithUTF8String:", text));
            var url = Call("NSURL", "fileURLWithPath:", path);
            return Call("SMAppService", "statusForLegacyURL:", url);
        }

        // A class method with one argument
        private static nint Call(string type, string method, nint argument) =>
            Send(WithText(type, objc_getClass), WithText(method, sel_registerName), argument);

        private static nint WithText(string value, Func<nint, nint> use)
        {
            var text = Marshal.StringToCoTaskMemUTF8(value);
            try
            {
                return use(text);
            }
            finally
            {
                Marshal.FreeCoTaskMem(text);
            }
        }

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern nint Send(nint receiver, nint selector, nint argument);

        [DllImport(ObjC)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern nint objc_getClass(nint name);

        [DllImport(ObjC)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern nint sel_registerName(nint name);
    }
}
