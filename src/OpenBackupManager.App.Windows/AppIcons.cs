namespace OpenBackupManager.App.Windows;

// The app icon's files, for its windows and notifications
internal static class AppIcons
{
    public static string Ico { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    // Its 256-pixel size, for showing it larger, since an image control only shows an .ico's first, smallest size.
    // Not needed once WinUI has https://github.com/microsoft/microsoft-ui-xaml/pull/12506
    public static string Png { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png");
}
