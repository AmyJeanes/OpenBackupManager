using Microsoft.UI.Xaml;

namespace OpenBackupManager.App.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = AppTitleBar.Title = Program.AppName;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        FirstDraw.HideUntilDrawn(this);
    }
}
