namespace OpenBackupManager.ViewModels;

// The window shown at the next start after the app closed unexpectedly
public sealed class CrashViewModel(AppInfo app, string report)
{
    public string Title { get; } = Text.Format(Strings.Crash_Title, app.Name);

    public string Report { get; } = report;
}
