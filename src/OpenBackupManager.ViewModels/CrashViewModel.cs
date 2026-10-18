using OpenBackupManager.Core;

namespace OpenBackupManager.ViewModels;

// The window shown once the app or its engine has closed unexpectedly
public sealed class CrashViewModel(AppInfo app, CrashReport report)
{
    public string Title { get; } = Text.Format(report.Name == CrashReports.Engine ? Strings.Crash_EngineTitle : Strings.Crash_Title, app.Name);

    public string Report { get; } = report.Text;
}
