namespace OpenBackupManager.Core.Configuration;

/// <summary>
/// Keeps this device's settings up to date with config.toml, so a hand edit applies as soon as it's saved. An edit
/// with an error leaves the last good settings in use until it's fixed
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    // How long the file has to be left alone before it's read, so a save in progress isn't read half written
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(250);

    private readonly ConfigFile _file;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _lock = new();
    private readonly Timer _settled;
    private string? _text;
    private bool _disposed;

    public ConfigWatcher(ConfigFile file)
    {
        _file = file;
        _settled = new Timer(_ => Reload());
        var folder = Path.GetDirectoryName(file.Path)!;
        Directory.CreateDirectory(folder);
        _text = ReadText();
        Result = Parse(_text);
        Config = Result.Config ?? new AppConfig();
        Watch(file.Path);
        // A dotfiles manager often links the file, or its folder, to the copy it keeps, and edits go to that copy
        if (LinkTarget(file.Path) is { } target && Path.GetDirectoryName(target) != folder)
        {
            Watch(target);
        }
    }

    // Raised from a background thread when the file has changed, with what reading it found
    public event Action<ConfigResult>? Changed;

    // The last settings read without errors, or the defaults if there haven't been any
    public AppConfig Config { get; private set; }

    // What the latest read found, including problems with an edit that's waiting to be fixed
    public ConfigResult Result { get; private set; }

    public void Reload()
    {
        ConfigResult result;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            string? text;
            try
            {
                text = ReadText();
            }
            catch (IOException)
            {
                // Still being saved. The save raises another change when it's done
                return;
            }

            // Saving often empties the file before writing it. Deleting it is how to go back to the defaults
            if (text == _text || string.IsNullOrWhiteSpace(text) && text is not null)
            {
                return;
            }

            _text = text;
            result = Parse(text);
            Result = result;
            Config = result.Config ?? Config;
        }

        Changed?.Invoke(result);
    }

    // Waits for a read in progress, so the file isn't in use once this returns
    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        lock (_lock)
        {
            _disposed = true;
        }

        _settled.Dispose();
    }

    // Where the file really is, if it or its folder is a link
    private static string? LinkTarget(string path)
    {
        try
        {
            if (new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true) is { } file)
            {
                return file.FullName;
            }

            return Directory.ResolveLinkTarget(Path.GetDirectoryName(path)!, returnFinalTarget: true) is { } folder
                ? Path.Combine(folder.FullName, Path.GetFileName(path))
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private void Watch(string path)
    {
        var watcher = new FileSystemWatcher(Path.GetDirectoryName(path)!, Path.GetFileName(path))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, _) => ReloadSoon();
        watcher.Created += (_, _) => ReloadSoon();
        watcher.Deleted += (_, _) => ReloadSoon();
        // Editors often save to another file and rename it over this one
        watcher.Renamed += (_, _) => ReloadSoon();
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    // Each change starts the wait again, as one save raises several
    private void ReloadSoon()
    {
        lock (_lock)
        {
            if (!_disposed)
            {
                _settled.Change(SettleTime, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static ConfigResult Parse(string? text) => text is null ? new ConfigResult(new AppConfig(), []) : ConfigFile.Parse(text);

    private string? ReadText() => ConfigFile.ReadText(_file.Path);
}
