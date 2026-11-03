using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenBackupManager.Core.Platform;

// Secrets in one JSON file only this user can open, with nothing but its permissions to protect them. For when the
// OS's own store can't be used, such as on a server. Only the engine uses it, so there's one writer
public sealed class FileStore(string path) : ISecretStore, IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Path { get; } = path;

    public async Task<Secret?> ReadAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return (await LoadAsync(cancellationToken)).TryGetValue(name, out var value) ? new Secret(value) : null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(string name, Secret secret, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var secrets = await LoadAsync(cancellationToken);
            secrets[name] = secret.Reveal();
            await WriteAsync(secrets, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        SecretName.Check(name);
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var secrets = await LoadAsync(cancellationToken);
            if (!secrets.Remove(name))
            {
                return false;
            }

            await WriteAsync(secrets, cancellationToken);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(Path, cancellationToken);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return [];
        }

        // Refused rather than started afresh, which would lose every secret in it on the next save
        Dictionary<string, string?> secrets;
        try
        {
            secrets = JsonSerializer.Deserialize(text, FileStoreJson.Default.DictionaryStringString)
                ?? throw new InvalidDataException($"{Path} is empty JSON");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path} isn't valid JSON, so its secrets can't be read. Fix it or remove it", e);
        }

        if (secrets.FirstOrDefault(secret => secret.Value is null).Key is { } empty)
        {
            throw new InvalidDataException($"{Path} has no value for {empty}. Fix it or remove it");
        }

        return secrets!;
    }

    // Written beside it then renamed over it, so a crash never leaves it half written
    private async Task WriteAsync(Dictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";

        // Made afresh, so a file or link someone else left in its place is never written through
        File.Delete(temporary);
        try
        {
            await using (var stream = Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, secrets, FileStoreJson.Default.DictionaryStringString, cancellationToken);

                // Or after a power cut the rename can be kept without what was written
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    private static FileStream Create(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.Asynchronous, OnlyThisUser());
        }

        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
    }

    // Its own permissions rather than the folder's, which may let others in
    [SupportedOSPlatform("windows")]
    private static FileSecurity OnlyThisUser()
    {
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var who in (IdentityReference[])[
            WindowsIdentity.GetCurrent().User!,
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null)])
        {
            security.AddAccessRule(new FileSystemAccessRule(who, FileSystemRights.FullControl, AccessControlType.Allow));
        }

        return security;
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string?>))]
internal sealed partial class FileStoreJson : JsonSerializerContext;
