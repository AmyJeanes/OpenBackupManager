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
        try
        {
            return JsonSerializer.Deserialize(text, FileStoreJson.Default.DictionaryStringString)
                ?? throw new InvalidDataException($"{Path} is empty JSON");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{Path} isn't valid JSON, so its secrets can't be read. Fix it or remove it", e);
        }
    }

    // Written beside it then renamed over it, so a crash never leaves it half written
    private async Task WriteAsync(Dictionary<string, string> secrets, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        await using (var stream = new FileStream(temporary, options))
        {
            await JsonSerializer.SerializeAsync(stream, secrets, FileStoreJson.Default.DictionaryStringString, cancellationToken);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class FileStoreJson : JsonSerializerContext;
