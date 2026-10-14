using Microsoft.UI.Xaml;
using OpenBackupManager.ViewModels;

namespace OpenBackupManager.App.Windows;

public sealed partial class MainWindow : Window
{
    public MainWindow(AppInfo appInfo)
    {
        AppInfo = appInfo;
        InitializeComponent();
        Title = AppTitleBar.Title = appInfo.Name;
        AppWindow.SetIcon(AppIcons.Ico);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        FirstDraw.HideUntilDrawn(this);
    }

    public AppInfo AppInfo { get; }
}
