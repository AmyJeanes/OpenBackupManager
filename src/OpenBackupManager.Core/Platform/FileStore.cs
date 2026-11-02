using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenBackupManager.Core.Platform;

// Secrets in one JSON file only this user can open, with nothing but its permissions to protect them. For when the
// OS's own store can't be used, such as on a server. Only the engine uses it, so there's one writer
public sealed class FileStore(string path) : ISecretStore
{
    private readonly Lock _lock = new();

    public string Path { get; } = path;

    public Secret? Read(string name)
    {
        lock (_lock)
        {
            return Load().TryGetValue(name, out var value) ? new Secret(value) : null;
        }
    }

    public void Save(string name, Secret secret)
    {
        lock (_lock)
        {
            var secrets = Load();
            secrets[name] = secret.Reveal();
            Write(secrets);
        }
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            var secrets = Load();
            if (!secrets.Remove(name))
            {
                return false;
            }

            Write(secrets);
            return true;
        }
    }

    private Dictionary<string, string> Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(Path);
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
    private void Write(Dictionary<string, string> secrets)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using (var stream = new FileStream(temporary, options))
        {
            JsonSerializer.Serialize(stream, secrets, FileStoreJson.Default.DictionaryStringString);
        }

        File.Move(temporary, Path, overwrite: true);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class FileStoreJson : JsonSerializerContext;
