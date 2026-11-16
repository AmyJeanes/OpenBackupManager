using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.ViewModels;

// Asks before the app quits, saying what happens to its engine. engine is who started it, or null when none is reached
public sealed class QuitViewModel(AppInfo app, EngineStarter? engine)
{
    public string AppName { get; } = app.Name;

    public string Title { get; } = Text.Format(Strings.Quit_Title, app.Name);

    public string Detail { get; } = engine switch
    {
        null => "",
        EngineStarter.App => Strings.Quit_EngineStops,
        _ => Strings.Quit_EngineKeepsRunning,
    };
}
