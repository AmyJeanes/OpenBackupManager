using System.Diagnostics;
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
        Title = AppTitleBar.Title = viewModel.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        this.SetWindowSize(720, 520);
        this.CenterOnScreen();
        FirstDraw.HideUntilDrawn(this);
    }

    public CrashViewModel ViewModel { get; }

    private void OnCopy(object sender, RoutedEventArgs e) => Copy(ViewModel.Report);

    private void OnReport(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ToPaste is { } report)
        {
            Copy(report);
        }

        Process.Start(new ProcessStartInfo(ViewModel.GitHubLink) { UseShellExecute = true })?.Dispose();
    }

    private static void Copy(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }
}
