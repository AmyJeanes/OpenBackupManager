using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.MacOS;

// Through Apple's security tool rather than Security.framework, as macOS lets the tool read what it saved after an
// update without a password prompt, which it doesn't for an unsigned build. Only tests pass a keychain file
[SupportedOSPlatform("macos")]
public sealed class Keychain(string prefix, string? file = null) : ISecretStore
{
    // security -i reads up to 4,095 bytes of a line and runs the rest as another command, so the newline must fit too
    internal const int MaxCommand = 4094;

    // errSecItemNotFound, and errSecInteractionNotAllowed for a keychain locked where the tool can't ask to unlock it,
    // such as over SSH. The tool prints no message for that one
    private const int NotFound = 44;
    private const int Locked = 36;

    public Secret? Read(string name)
    {
        var (exitCode, output, error) = Security(null, ["find-generic-password", "-s", Service(name), "-a", name, "-w", .. File()]);
        return exitCode switch
        {
            0 => new Secret(Encoding.UTF8.GetString(Convert.FromBase64String(output.TrimEnd('\n')))),
            NotFound => null,
            _ => throw Failed("read", name, exitCode, error),
        };
    }

    public void Save(string name, Secret secret)
    {
        var command = SaveCommand(name, secret);
        if (Encoding.UTF8.GetByteCount(command) > MaxCommand)
        {
            throw new ArgumentException($"The Keychain's security tool takes commands of up to {MaxCommand} bytes, and saving this secret needs more", nameof(secret));
        }

        // Through its input, as any program can see a command line
        var (exitCode, _, error) = Security(command + "\n", ["-i"]);
        if (exitCode != 0)
        {
            throw Failed("save", name, exitCode, error);
        }
    }

    public bool Remove(string name)
    {
        var (exitCode, _, error) = Security(null, ["delete-generic-password", "-s", Service(name), "-a", name, .. File()]);
        return exitCode switch
        {
            0 => true,
            NotFound => false,
            _ => throw Failed("remove", name, exitCode, error),
        };
    }

    // Base64, as -w prints a secret with anything outside printable ASCII as hex, which can't be told apart from text.
    // It also needs no quoting
    internal string SaveCommand(string name, Secret secret) =>
        string.Join(' ', [
            "add-generic-password", "-U", "-s", Quote(Service(name)), "-a", Quote(name),
            "-w", Convert.ToBase64String(Encoding.UTF8.GetBytes(secret.Reveal())),
            .. File().Select(Quote)]);

    private string Service(string name) => $"{prefix}/{name}";

    private string[] File() => file is null ? [] : [file];

    // security -i splits a line with its own quoting, where a backslash escapes the next character
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static InvalidOperationException Failed(string action, string name, int exitCode, string error) =>
        new($"The Keychain couldn't {action} {name}: " + (exitCode == Locked ? "it's locked" : $"{error.Trim()} ({exitCode})"));

    private static (int ExitCode, string Output, string Error) Security(string? input, string[] arguments)
    {
        var start = new ProcessStartInfo("/usr/bin/security")
        {
            RedirectStandardInput = input is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }

        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.Result);
    }
}
