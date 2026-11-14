using System.Globalization;
using Microsoft.Extensions.Logging;

namespace OpenBackupManager.Core.Configuration;

/// <summary>
/// Keeps this device's settings up to date with config.toml, so a hand edit applies as soon as it's saved. An edit
/// with an error, or a file that can't be read, leaves the last good settings in use until it's fixed. An error in the
/// first read throws a ConfigException instead, as there are no good settings to fall back on
/// </summary>
public sealed partial class ConfigWatcher : IDisposable
{
    // How long the file has to be left alone before it's read, so a save in progress isn't read half written
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(250);

    private readonly ConfigFile _file;
    private readonly ILogger _logger;
    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly Lock _lock = new();
    private readonly Timer _settled;
    private string? _text;
    private bool _disposed;

    public ConfigWatcher(ConfigFile file, ILogger<ConfigWatcher> logger)
    {
        _file = file;
        _logger = logger;
        var folder = Path.GetDirectoryName(file.Path)!;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw CantRead(e);
        }

        _text = ReadText();
        Result = Parse(_text);
        if (Result.Problems.FirstOrDefault(p => p.IsError) is { } error)
        {
            throw new ConfigException(error.EnvVar is null
                ? $"{file.Path} has an error on line {error.Line}: {error.Message(CultureInfo.InvariantCulture)}"
                : $"{error.EnvVar} has an error: {error.Message(CultureInfo.InvariantCulture)}", error);
        }

        Config = Result.Config!;
        Log(Result);
        _settled = new Timer(_ => TryReload(saving: true));
        try
        {
            Watch(file.Path);
            // A dotfiles manager often links the file, or its folder, to the copy it keeps, and edits go to that copy
            if (LinkTarget(file.Path) is { } target && Path.GetDirectoryName(target) != folder)
            {
                Watch(target);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Such as when other programs have used up the user's inotify instances, which a restart wouldn't fix
            LogCantWatch(file.Path, e.Message);
        }
    }

    // Raised from a background thread when the file has changed, with what reading it found
    public event Action<ConfigResult>? Changed;

    // The last settings read without errors
    public AppConfig Config { get; private set; }

    // What the latest read found, including problems with an edit that's waiting to be fixed
    public ConfigResult Result { get; private set; }

    // Applied straight away rather than when the watcher sees it, so what reads the setting next gets the new value
    public void Set(string? table, string key, object? value)
    {
        _file.Set(table, key, value);
        Reload();
    }

    // When the setting's env var is set, which wins over the file, so changing the file wouldn't change the setting
    public string? EnvVarOverriding(string path) =>
        Settings.Find(path)?.EnvVar is { } envVar && _file.Environment(envVar) is { Length: > 0 } ? envVar : null;

    // Throws a ConfigException when the file can't be read
    public void Reload()
    {
        ConfigResult result;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            var text = ReadText();
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

        if (!result.HasErrors)
        {
            LogApplied();
        }

        Log(result);
        Changed?.Invoke(result);
    }

    // For what has only the log to say what's wrong, such as SIGHUP
    public void TryReload() => TryReload(saving: false);

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
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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
        // Before it starts, which can fail, so it's still disposed
        _watchers.Add(watcher);
        watcher.Changed += (_, _) => ReloadSoon();
        watcher.Created += (_, _) => ReloadSoon();
        watcher.Deleted += (_, _) => ReloadSoon();
        // Editors often save to another file and rename it over this one
        watcher.Renamed += (_, _) => ReloadSoon();
        watcher.EnableRaisingEvents = true;
    }

    private void TryReload(bool saving)
    {
        try
        {
            Reload();
        }
        catch (ConfigException e) when (saving && e.InnerException is IOException)
        {
            // On Windows, still being saved by an editor that keeps others out while it writes. The save raises another
            // change when it's done
        }
        catch (ConfigException e)
        {
            LogCantRead(e.Message);
        }
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

    private ConfigResult Parse(string? text) => ConfigFile.Parse(text ?? "", _file.Environment);

    private string? ReadText()
    {
        try
        {
            return ConfigFile.ReadText(_file.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw CantRead(e);
        }
    }

    // Such as when it belongs to another user
    private ConfigException CantRead(Exception e) => new($"{_file.Path} can't be read: {e.Message.TrimEnd('.')}", inner: e);

    private void Log(ConfigResult result)
    {
        foreach (var problem in result.Problems)
        {
            if (problem.IsError)
            {
                LogError(problem);
            }
            else
            {
                LogWarning(problem);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied the changes to config.toml")]
    private partial void LogApplied();

    [LoggerMessage(Level = LogLevel.Error, Message = "config.toml {Problem}, so the last good settings stay in use")]
    private partial void LogError(ConfigProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "config.toml {Problem}")]
    private partial void LogWarning(ConfigProblem problem);

    [LoggerMessage(Level = LogLevel.Error, Message = "{Reason}, so the last good settings stay in use")]
    private partial void LogCantRead(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Can't watch {Path} for edits, so they apply only when it's read again, such as by obm config reload or a restart. {Reason}")]
    private partial void LogCantWatch(string path, string reason);
}
