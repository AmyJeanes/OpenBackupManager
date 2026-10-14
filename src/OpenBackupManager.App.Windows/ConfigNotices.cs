using System.Globalization;
using Microsoft.Extensions.Logging;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

// Says what's wrong with config.toml when it's read: every problem in the log, and an error in a notification too
public sealed partial class ConfigNotices : IDisposable
{
    private readonly ConfigWatcher _config;
    private readonly AppNotifications _notifications;
    private readonly ILogger _logger;

    public ConfigNotices(ConfigWatcher config, AppNotifications notifications, ILogger<ConfigNotices> logger)
    {
        _config = config;
        _notifications = notifications;
        _logger = logger;
        Report(config.Result);
        config.Changed += OnChanged;
    }

    public void Dispose() => _config.Changed -= OnChanged;

    private void OnChanged(ConfigResult result)
    {
        if (!result.HasErrors)
        {
            LogApplied();
        }

        Report(result);
    }

    private void Report(ConfigResult result)
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

        if (result.Problems.FirstOrDefault(p => p.IsError) is { } error)
        {
            _notifications.ShowConfigError(error.Describe(CultureInfo.CurrentUICulture));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied the changes to config.toml")]
    private partial void LogApplied();

    [LoggerMessage(Level = LogLevel.Error, Message = "config.toml {Problem}, so the last good settings stay in use")]
    private partial void LogError(ConfigProblem problem);

    [LoggerMessage(Level = LogLevel.Warning, Message = "config.toml {Problem}")]
    private partial void LogWarning(ConfigProblem problem);
}
