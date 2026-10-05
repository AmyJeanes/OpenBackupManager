using System.CommandLine;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Bcpg.OpenPgp;

var versionFileOption = new Option<FileInfo>("--version-file")
{
    Description = "The rclone.version file that pins the release",
    Required = true,
}.AcceptExistingOnly();
var keyFileOption = new Option<FileInfo>("--key")
{
    Description = "rclone's public signing key, ASCII-armored",
    Required = true,
}.AcceptExistingOnly();
var runtimeOption = new Option<string>("--runtime")
{
    Description = "The .NET runtime identifier to fetch rclone for, such as win-x64",
    Required = true,
};
var outputOption = new Option<FileInfo>("--output")
{
    Description = "Where to write the rclone binary",
    Required = true,
};

var command = new RootCommand(
    "Downloads the rclone release pinned in rclone.version for one runtime, and only writes the binary out once its hash "
    + "matches rclone's SHA256SUMS and that file's signature matches the pinned key")
{
    versionFileOption,
    keyFileOption,
    runtimeOption,
    outputOption,
};

command.SetAction(async (parseResult, cancellationToken) =>
{
    var runtime = parseResult.GetRequiredValue(runtimeOption);
    try
    {
        var version = ReadVersion(parseResult.GetRequiredValue(versionFileOption).FullName);
        var asset = $"rclone-{version}-{RcloneTarget(runtime)}.zip";
        var releaseUrl = $"https://github.com/rclone/rclone/releases/download/{version}/";

        using var http = new HttpClient();
        var signedSums = await http.GetByteArrayAsync(new Uri(releaseUrl + "SHA256SUMS"), cancellationToken);
        var sums = VerifyClearSigned(signedSums, parseResult.GetRequiredValue(keyFileOption).FullName);
        var expected = FindHash(sums, asset);
        var zip = await http.GetByteArrayAsync(new Uri(releaseUrl + asset), cancellationToken);
        var actual = Convert.ToHexStringLower(SHA256.HashData(zip));
        if (actual != expected)
        {
            throw new InvalidDataException($"{asset} has SHA256 {actual}, but the signed SHA256SUMS says {expected}");
        }

        Extract(zip, parseResult.GetRequiredValue(outputOption).FullName);
        Console.WriteLine($"rclone {version} for {runtime}: signature and hash verified");
        return 0;
    }
    catch (Exception e) when (e is InvalidDataException or ArgumentException or IOException or HttpRequestException or PgpException)
    {
        Console.Error.WriteLine($"RcloneFetch: {e.Message}");
        return 1;
    }
});

return await command.Parse(args).InvokeAsync();

static string ReadVersion(string versionFile) =>
    File.ReadLines(versionFile)
        .Select(line => line.Trim())
        .Single(line => line.Length > 0 && !line.StartsWith('#'));

static string RcloneTarget(string runtime)
{
    var dash = runtime.LastIndexOf('-');
    var os = dash < 0 ? runtime : runtime[..dash];
    var arch = runtime[(dash + 1)..];
    var rcloneOs = os switch
    {
        "win" => "windows",
        "osx" => "osx",
        "linux" or "linux-musl" => "linux",
        _ => throw new ArgumentException($"rclone isn't bundled for runtime '{runtime}'"),
    };
    var rcloneArch = arch switch
    {
        "x64" => "amd64",
        "arm64" => "arm64",
        _ => throw new ArgumentException($"rclone isn't bundled for runtime '{runtime}'"),
    };
    return $"{rcloneOs}-{rcloneArch}";
}

// Returns the signed text of a cleartext-signed file, after checking its signature against the key file
static string VerifyClearSigned(byte[] signedFile, string keyFile)
{
    using var armored = new ArmoredInputStream(new MemoryStream(signedFile));
    var lines = ReadClearTextLines(armored);
    if (new PgpObjectFactory(armored).NextPgpObject() is not PgpSignatureList { Count: > 0 } signatures)
    {
        throw new InvalidDataException("SHA256SUMS isn't signed");
    }

    var signature = signatures[0];
    using var keyStream = PgpUtilities.GetDecoderStream(File.OpenRead(keyFile));
    var key = new PgpPublicKeyRingBundle(keyStream).GetPublicKey(signature.KeyId)
        ?? throw new InvalidDataException($"SHA256SUMS is signed by key {signature.KeyId:X16}, not the pinned rclone key");

    // OpenPGP signs cleartext with CRLF line endings, trailing whitespace removed and no final line ending
    signature.InitVerify(key);
    signature.Update(Encoding.UTF8.GetBytes(string.Join("\r\n", lines.Select(line => line.TrimEnd(' ', '\t')))));
    if (!signature.Verify())
    {
        throw new InvalidDataException("SHA256SUMS doesn't match its signature");
    }

    return string.Join("\n", lines);
}

// ArmoredInputStream undoes the dash-escaping and reports when the cleartext gives way to the signature
static List<string> ReadClearTextLines(ArmoredInputStream armored)
{
    var lines = new List<string>();
    var line = new MemoryStream();
    int next;
    while ((next = armored.ReadByte()) >= 0 && armored.IsClearText())
    {
        if (next == '\n')
        {
            lines.Add(Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'));
            line.SetLength(0);
        }
        else
        {
            line.WriteByte((byte)next);
        }
    }

    return lines;
}

static string FindHash(string sums, string asset) =>
    sums.Split('\n')
        .Select(line => line.Split("  ", 2))
        .FirstOrDefault(parts => parts.Length == 2 && parts[1] == asset)?[0]
    ?? throw new InvalidDataException($"The signed SHA256SUMS has no entry for {asset}");

static void Extract(byte[] zip, string outputFile)
{
    using var archive = new ZipArchive(new MemoryStream(zip));
    var name = Path.GetFileName(outputFile);
    var entry = archive.Entries.Single(e => e.Name == name);

    // Written under a temporary name first, so an interrupted fetch never looks complete to the build
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))
        ?? throw new ArgumentException($"'{outputFile}' isn't a file path"));
    var partial = outputFile + ".partial";
    entry.ExtractToFile(partial, overwrite: true);

    // The zip's own date would make the build always see the file as older than rclone.version and fetch it again
    File.SetLastWriteTimeUtc(partial, DateTime.UtcNow);
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(partial, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    File.Move(partial, outputFile, overwrite: true);
}
