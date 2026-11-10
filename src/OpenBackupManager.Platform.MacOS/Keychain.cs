using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using OpenBackupManager.Core;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.Platform.MacOS;

// Through Apple's security tool rather than Security.framework, as macOS lets the tool read what it saved after an
// update without a password prompt, which it doesn't for an unsigned build
[SupportedOSPlatform("macos")]
public sealed class Keychain(string prefix, string file) : ISecretStore
{
    // Named in every command rather than left to the default keychain and search list, which the user can change
    public static readonly string Login = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Keychains", "login.keychain-db");

    // security -i reads up to 4,095 bytes of a line and runs the rest as another command, so the newline must fit too
    // https://github.com/apple-oss-distributions/Security/blob/Security-61901.120.67/SecurityTool/macOS/security.c#L79
    internal const int MaxCommand = 4094;

    // errSecItemNotFound, and errSecInteractionNotAllowed for a keychain locked where the tool can't ask to unlock it,
    // such as over SSH. The tool prints no message for that one
    private const int NotFound = 44;
    private const int Locked = 36;

    // The tool answers at once, so one that doesn't in this time isn't going to
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public async Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        var (exitCode, output, error) = await SecurityAsync("read", name, null, ["find-generic-password", "-s", Service(name), "-a", name, "-w", file], cancellationToken);
        return exitCode switch
        {
            0 => new Secret(Encoding.UTF8.GetString(Convert.FromBase64String(output.TrimEnd('\n')))),
            NotFound => null,
            _ => throw Failed("read", name, exitCode, error),
        };
    }

    public async Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        var command = SaveCommand(name, secret);
        if (Encoding.UTF8.GetByteCount(command) > MaxCommand)
        {
            throw new ArgumentException($"The Keychain's security tool takes commands of up to {MaxCommand} bytes, and saving this secret needs more", nameof(secret));
        }

        // Through its input, as any program can see a command line
        var (exitCode, _, error) = await SecurityAsync("save", name, command + "\n", ["-i"], cancellationToken);
        if (exitCode != 0)
        {
            throw Failed("save", name, exitCode, error);
        }
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        var (exitCode, _, error) = await SecurityAsync("remove", name, null, ["delete-generic-password", "-s", Service(name), "-a", name, file], cancellationToken);
        return exitCode switch
        {
            0 => true,
            NotFound => false,
            _ => throw Failed("remove", name, exitCode, error),
        };
    }

    // Without -d it prints every item's attributes and none of their secrets, so it never asks to unlock anything
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var (exitCode, output, error) = await SecurityAsync("list", "the saved secrets", null, ["dump-keychain", file], cancellationToken);
        return exitCode == 0 ? Names(output) : throw Failed("list", "the saved secrets", exitCode, error);
    }

    // Each item starts with a keychain: line. Anything can save an item, so only those named as this store names them count
    private IReadOnlyList<string> Names(string dump)
    {
        var names = new List<string>();
        string? service = null, account = null;
        foreach (var line in dump.Split('\n').Append("keychain: "))
        {
            if (line.StartsWith("keychain: ", StringComparison.Ordinal))
            {
                if (account is not null && SecretName.IsValid(account) && service == Service(account))
                {
                    names.Add(account);
                }

                service = account = null;
            }

            service = Attribute(line, "svce") ?? service;
            account = Attribute(line, "acct") ?? account;
        }

        return [.. names.Distinct().Order(StringComparer.Ordinal)];
    }

    // Base64, as -w prints a secret with anything outside printable ASCII as hex, which can't be told apart from text.
    // Quoted all the same, as an empty secret would leave -w with no value, and the tool would ask for one
    internal string SaveCommand(string name, Secret secret) =>
        string.Join(' ',
            "add-generic-password", "-U", "-s", Quote(Service(name)), "-a", Quote(name),
            "-w", Quote(Convert.ToBase64String(Encoding.UTF8.GetBytes(secret.Reveal()))), Quote(file));

    private string Service(string name) => $"{prefix}/{name}";

    // Such as     "acct"<blob>="rclone-config". A value that isn't plain text is printed as hex, which no name of ours is
    private static string? Attribute(string line, string attribute)
    {
        var start = $"    \"{attribute}\"<blob>=\"";
        return line.StartsWith(start, StringComparison.Ordinal) && line.EndsWith('"') ? line[start.Length..^1] : null;
    }

    // security -i splits a line with its own quoting, where a backslash escapes the next character
    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static InvalidOperationException Failed(string action, string name, int exitCode, string error) =>
        Failed(action, name, exitCode == Locked ? "it's locked" : $"{error.Trim()} ({exitCode})");

    private static InvalidOperationException Failed(string action, string name, string reason) =>
        new($"The Keychain couldn't {action} {name}: {reason}");

    private static async Task<(int ExitCode, string Output, string Error)> SecurityAsync(
        string action, string name, string? input, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(SystemPrograms.Find("security"))
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
        using var stdout = process.StandardOutput;
        using var stderr = process.StandardError;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            if (input is not null)
            {
                await process.StandardInput.WriteAsync(input.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }

            var error = stderr.ReadToEndAsync(timeout.Token);
            var output = await stdout.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, output, await error);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failed(action, name, $"it didn't answer within {Timeout.TotalSeconds} seconds");
        }
        finally
        {
            // So a cancelled or stuck call doesn't leave it running
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
    }
}
