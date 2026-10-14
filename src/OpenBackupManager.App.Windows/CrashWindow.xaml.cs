using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenBackupManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class CrashWindow : Window
{
    public CrashWindow(CrashViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.SetIcon(AppIcons.Ico);
        Title = viewModel.Title;
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        this.SetWindowSize(720, 520);
        this.CenterOnScreen();
        FirstDraw.HideUntilDrawn(this);
    }

    public CrashViewModel ViewModel { get; }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ViewModel.Report);
        Clipboard.SetContent(package);
    }
}
