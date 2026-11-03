using System.CommandLine;
using NoticesGen;

var runtimeOption = new Option<string>("--runtime")
{
    Description = "The .NET runtime identifier the programs were published for, such as win-x64",
    Required = true,
};
var handWrittenOption = new Option<FileInfo>("--hand-written")
{
    Description = "The notices kept by hand, which go first",
    Required = true,
}.AcceptExistingOnly();
var assetsOption = new Option<FileInfo[]>("--assets")
{
    Description = "Each published program's obj/project.assets.json",
    Required = true,
    AllowMultipleArgumentsPerToken = true,
}.AcceptExistingOnly();
var outputOption = new Option<FileInfo>("--output")
{
    Description = "Where to write the notices",
    Required = true,
};

var command = new RootCommand(
    "Writes the third-party notices for programs just published: the hand-written ones, then the licence of every NuGet "
    + "package with files in the programs")
{
    runtimeOption,
    handWrittenOption,
    assetsOption,
    outputOption,
};

command.SetAction(parseResult =>
{
    try
    {
        var notices = Notices.Write(
            File.ReadAllText(parseResult.GetRequiredValue(handWrittenOption).FullName),
            parseResult.GetRequiredValue(assetsOption).Select(file => file.FullName),
            parseResult.GetRequiredValue(runtimeOption));
        var output = parseResult.GetRequiredValue(outputOption).FullName;
        File.WriteAllText(output, notices);
        Console.WriteLine($"Wrote {output}");
        return 0;
    }
    catch (Exception e) when (e is InvalidDataException or IOException)
    {
        Console.Error.WriteLine($"NoticesGen: {e.Message}");
        return 1;
    }
});

return await command.Parse(args).InvokeAsync();
