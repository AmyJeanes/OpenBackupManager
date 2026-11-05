using OpenBackupManager.Core;

namespace OpenBackupManager.ViewModels;

// The window shown once the app or its engine has closed unexpectedly
public sealed class CrashViewModel(AppInfo app, CrashReport report)
{
    private readonly string _shared = Redaction.Redact(report.Text);

    public string AppName { get; } = app.Name;

    public string Title { get; } = Text.Format(report.Name == CrashReports.Engine ? Strings.Crash_EngineTitle : Strings.Crash_Title, app.Name);

    public string Report { get; } = report.Text;

    // What Report on GitHub puts on the clipboard first, when it's too long for the link, as VS Code's issue reporter does
    public string? ToPaste => BugReport.Fits(_shared, _shared) ? null : _shared;

    // A new GitHub issue, filled in
    public string GitHubLink => BugReport.Link(_shared, ToPaste is null ? _shared : Strings.Crash_PasteHere);
}
