using System.Text.Json;

namespace NoticesGen.Tests;

public class NoticesTests
{
    private const string HandWritten = "Third-party notices\n\nWritten by hand.\n";
    private const string Runtime = "linux-x64";
    private const string Mit = "Permission is hereby granted, free of charge";

    private string _folder = "";

    [SetUp]
    public void CreateFolder() => _folder = Directory.CreateTempSubdirectory("obm-notices-").FullName;

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void TheHandWrittenNoticesComeFirst()
    {
        var notices = Write(Assets(Package("Tomlyn", License("BSD-2-Clause"), "Alexandre Mutel")));

        Assert.That(notices, Does.StartWith(HandWritten.ReplaceLineEndings()));
    }

    [Test]
    public void OnlyPackagesWithFilesThatRun_AreListed()
    {
        var notices = Write(Assets(
            Package("Runs", License("MIT")),
            Package("Native", License("MIT"), kind: "native", file: "runtimes/linux-x64/native/libnative.so"),
            Package("Translations", License("MIT"), kind: "resource", file: "lib/net10.0/de/Translations.resources.dll"),
            Package("Analyzer", License("MIT"), kind: "build", file: "build/Analyzer.targets"),
            Package("Empty", License("MIT"), file: "lib/net10.0/_._")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notices, Does.Contain("Runs 1.0.0\n".ReplaceLineEndings()));
            Assert.That(notices, Does.Contain("Native 1.0.0\n".ReplaceLineEndings()));
            Assert.That(notices, Does.Contain("Translations 1.0.0\n".ReplaceLineEndings()));
            Assert.That(notices, Does.Not.Contain("Analyzer"));
            Assert.That(notices, Does.Not.Contain("Empty"));
        }
    }

    [Test]
    public void ProjectsTheProgramsReference_AreNotListed()
    {
        var notices = Write(Assets(Package("Runs", License("MIT")), Package("OpenBackupManager.Core", License("MIT"), type: "project")));

        Assert.That(notices, Does.Not.Contain("OpenBackupManager.Core"));
    }

    [Test]
    public void OnlyThePackagesForTheRuntime_AreListed()
    {
        var linux = Package("Tmds.DBus.Protocol", License("MIT"));
        var windows = Package("Microsoft.Win32.SystemEvents", License("MIT"));
        var assets = Assets(linux);
        AddTarget(assets, "net10.0/win-x64", windows);

        var notices = Write(assets);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notices, Does.Contain("Tmds.DBus.Protocol"));
            Assert.That(notices, Does.Not.Contain("Microsoft.Win32.SystemEvents"));
        }
    }

    [Test]
    public void AStandardLicence_IsWrittenOnce_UnderEveryPackageAndItsCopyright()
    {
        var notices = Write(Assets(
            Package("Velopack", License("MIT"), "Copyright © Velopack Ltd."),
            Package("CommunityToolkit.Mvvm", License("MIT"), "(c) .NET Foundation and Contributors")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Generated(notices), Does.StartWith(
                "These NuGet packages are used under the MIT licence:\n\n"
                + "CommunityToolkit.Mvvm 1.0.0: (c) .NET Foundation and Contributors\n"
                + "Velopack 1.0.0: Copyright © Velopack Ltd.\n\n"
                + Mit));
            Assert.That(Count(notices, Mit), Is.EqualTo(1));
        }
    }

    [Test]
    public void EachLicence_GetsItsOwnText()
    {
        var notices = Write(Assets(
            Package("Serilog", License("Apache-2.0"), "Copyright © Serilog Contributors"),
            Package("Serilog.Sinks.File", License("Apache-2.0")),
            Package("Tomlyn", License("BSD-2-Clause"), "Alexandre Mutel")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notices, Does.Contain("Serilog 1.0.0: Copyright © Serilog Contributors\nSerilog.Sinks.File 1.0.0\n\n".ReplaceLineEndings()));
            Assert.That(Count(notices, "Apache License"), Is.EqualTo(1));
            Assert.That(notices, Does.Contain("Tomlyn 1.0.0: Alexandre Mutel\n\nRedistribution and use".ReplaceLineEndings()));
        }
    }

    [Test]
    public void APackagesOwnLicenceFile_IsUsedAsItIs_AndSharedWithPackagesWithTheSameFile()
    {
        const string Terms = "MICROSOFT SOFTWARE LICENSE TERMS\r\nMICROSOFT WINDOWS APP SDK\r\n";
        var notices = Write(Assets(
            Package("Microsoft.WindowsAppSDK.WinUI", License("license.txt", file: true), "© Microsoft Corporation.", files: [("license.txt", Terms)]),
            Package("Microsoft.WindowsAppSDK.Foundation", License("license.txt", file: true), "© Microsoft Corporation.", files: [("license.txt", Terms.ReplaceLineEndings("\n"))])));

        Assert.That(Generated(notices), Is.EqualTo(
            "These NuGet packages are used under the licence they come with:\n\n"
            + "Microsoft.WindowsAppSDK.Foundation 1.0.0: © Microsoft Corporation.\n"
            + "Microsoft.WindowsAppSDK.WinUI 1.0.0: © Microsoft Corporation.\n\n"
            + "MICROSOFT SOFTWARE LICENSE TERMS\nMICROSOFT WINDOWS APP SDK\n"));
    }

    [Test]
    public void AnApachePackagesNoticeFile_IsPassedOn()
    {
        var notices = Write(Assets(Package("Apache.Thing", License("Apache-2.0"), files: [("NOTICE.txt", "Apache Thing\nIncludes work by others.\n")])));

        Assert.That(notices, Does.Contain("Apache.Thing 1.0.0\n\nApache.Thing's NOTICE file:\n\nApache Thing\nIncludes work by others.\n\n".ReplaceLineEndings()));
    }

    [Test]
    public void APackageInSeveralPrograms_IsListedOnce()
    {
        var obm = Assets(Package("System.CommandLine", License("MIT")));
        var obmd = Assets(Package("System.CommandLine", License("MIT")), Package("Serilog", License("Apache-2.0")));

        var notices = Notices.Write(HandWritten, [Save(obm, "obm"), Save(obmd, "obmd")], Runtime);

        Assert.That(Count(notices, "System.CommandLine 1.0.0"), Is.EqualTo(1));
    }

    [Test]
    public void ALicenceWithoutATemplate_Stops_NamingThePackage()
    {
        var assets = Assets(Package("Copyleft", License("GPL-3.0-only")));

        Assert.That(() => Write(assets), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Copyleft 1.0.0").And.Message.Contains("GPL-3.0-only"));
    }

    [Test]
    public void APackageWithNoLicence_Stops_NamingIt()
    {
        var assets = Assets(Package("Unlicensed", "<licenseUrl>https://example.com/licence</licenseUrl>"));

        Assert.That(() => Write(assets), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Unlicensed 1.0.0"));
    }

    [Test]
    public void ProgramsNotRestoredForTheRuntime_Stop()
    {
        var assets = Assets(Package("Runs", License("MIT")));

        Assert.That(() => Notices.Write(HandWritten, [Save(assets, "obm")], "osx-arm64"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("osx-arm64"));
    }

    private static string License(string value, bool file = false) =>
        $"<license type=\"{(file ? "file" : "expression")}\">{value}</license>";

    private static int Count(string text, string part) => text.Split(part).Length - 1;

    // What follows the hand-written notices and the rule after them
    private static string Generated(string notices) =>
        notices.ReplaceLineEndings("\n").Split("--------------------------------------------------------------------------------\n\n", 2)[1];

    private string Write(Dictionary<string, object> assets) => Notices.Write(HandWritten, [Save(assets, "obm")], Runtime);

    private string Save(Dictionary<string, object> assets, string program)
    {
        var file = Path.Combine(_folder, program, "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(assets));
        return file;
    }

    // An assets file laid out as NuGet writes it, with a target for the framework and Runtime
    private Dictionary<string, object> Assets(params Library[] libraries)
    {
        var assets = new Dictionary<string, object>
        {
            ["targets"] = new Dictionary<string, object> { ["net10.0"] = new Dictionary<string, object>() },
            ["libraries"] = new Dictionary<string, object>(),
            ["packageFolders"] = new Dictionary<string, object> { [Path.Combine(_folder, "packages") + Path.DirectorySeparatorChar] = new { } },
        };
        AddTarget(assets, $"net10.0/{Runtime}", libraries);
        return assets;
    }

    private static void AddTarget(Dictionary<string, object> assets, string name, params Library[] libraries)
    {
        var targets = (Dictionary<string, object>)assets["targets"];
        var entries = (Dictionary<string, object>)assets["libraries"];
        targets[name] = libraries.ToDictionary(
            library => library.Name,
            object (library) => new Dictionary<string, object>
            {
                ["type"] = library.Type,
                [library.Kind] = new Dictionary<string, object> { [library.File] = new { } },
            });
        foreach (var library in libraries)
        {
            entries[library.Name] = new { type = library.Type, path = library.Path };
        }
    }

    // A package in the package folder, with its nuspec and any other files
    private Library Package(
        string id,
        string licence,
        string? copyright = null,
        string kind = "runtime",
        string? file = null,
        string type = "package",
        (string Name, string Text)[]? files = null)
    {
        var path = $"{id.ToLowerInvariant()}/1.0.0";
        var folder = Path.Combine(_folder, "packages", path);
        Directory.CreateDirectory(folder);
        var copyrightElement = copyright is null ? "" : $"<copyright>{copyright}</copyright>";
        File.WriteAllText(Path.Combine(folder, $"{id.ToLowerInvariant()}.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>1.0.0</version>
                {licence}
                {copyrightElement}
              </metadata>
            </package>
            """);
        foreach (var (name, text) in files ?? [])
        {
            File.WriteAllText(Path.Combine(folder, name), text);
        }

        return new Library($"{id}/1.0.0", path, type, kind, file ?? $"lib/net10.0/{id}.dll");
    }

    private sealed record Library(string Name, string Path, string Type, string Kind, string File);
}
