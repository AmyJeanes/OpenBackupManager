using System.Globalization;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

// The watcher logs every problem, so this only notifies
public sealed class ConfigNotices : IDisposable
{
    private readonly ConfigWatcher _config;
    private readonly AppNotifications _notifications;

    public ConfigNotices(ConfigWatcher config, AppNotifications notifications)
    {
        _config = config;
        _notifications = notifications;
        config.Changed += OnChanged;
    }

    public void Dispose() => _config.Changed -= OnChanged;

    private void OnChanged(ConfigResult result)
    {
        if (result.Problems.FirstOrDefault(p => p.IsError) is { } error)
        {
            _notifications.ShowConfigError(error.Describe(CultureInfo.CurrentUICulture));
        }
    }
}
