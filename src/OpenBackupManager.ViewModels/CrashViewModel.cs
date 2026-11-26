using OpenBackupManager.Core;

namespace OpenBackupManager.ViewModels;

// The window shown once the app or its engine has closed unexpectedly
public sealed class CrashViewModel(AppInfo app, CrashReport report)
{
    public string AppName { get; } = app.Name;

    public string Title { get; } = Text.Format(report.Name == CrashReports.Engine ? Strings.Crash_EngineTitle : Strings.Crash_Title, app.Name);

    // Redacted, so what's shown is what's shared, by the buttons or by copying it by hand
    public string Report { get; } = Redaction.Redact(report.Text);

    // What Report on GitHub puts on the clipboard first, when too long for the link, as VS Code's issue reporter does
    public string? ToPaste => BugReport.Fits(Report, Report) ? null : Report;

    // A new GitHub issue, filled in
    public string GitHubLink => BugReport.Link(Report, ToPaste is null ? Report : Strings.Crash_PasteHere);
}
