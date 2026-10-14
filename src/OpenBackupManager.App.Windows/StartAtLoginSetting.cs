using System.Security;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.App.Windows;

// Starts the app at login, or not, as config.toml says. Left out, only the installed app's own home does, so dev builds,
// test copies and other homes never add themselves
public sealed partial class StartAtLoginSetting : IDisposable
{
    private readonly ConfigWatcher _config;
    private readonly IStartAtLogin _startAtLogin;
    private readonly bool _onByDefault;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();

    public StartAtLoginSetting(ConfigWatcher config, IStartAtLogin startAtLogin, bool onByDefault, ILogger<StartAtLoginSetting> logger)
    {
        _config = config;
        _startAtLogin = startAtLogin;
        _onByDefault = onByDefault;
        _logger = logger;
        Apply();
        config.Changed += OnConfigChanged;
    }

    public void Dispose() => _config.Changed -= OnConfigChanged;

    private void OnConfigChanged(ConfigResult _) => Apply();

    private void Apply()
    {
        // Changes can be raised from two threads at once
        lock (_lock)
        {
            var on = _config.Config.StartAtLogin ?? _onByDefault;
            bool was;
            try
            {
                was = _startAtLogin.IsOn;
                // Even when it already is, since an entry from where this copy used to be counts as off but still needs
                // replacing or removing
                if (on)
                {
                    _startAtLogin.TurnOn();
                }
                else
                {
                    _startAtLogin.TurnOff();
                }
            }
            catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
            {
                LogFailed(e, on);
                return;
            }

            if (on != was)
            {
                LogChanged(on);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting at login: {On}")]
    private partial void LogChanged(bool on);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't set starting at login to {On}")]
    private partial void LogFailed(Exception exception, bool on);
}
