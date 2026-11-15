using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using OpenBackupManager.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class CrashWindow : Window
{
    // CLIPBRD_E_CANT_OPEN, as another app has the clipboard open. Clipboard managers and remote desktop do, for moments
    private const int ClipboardBusy = unchecked((int)0x800401D0);
    private const int CopyTries = 5;
    private static readonly TimeSpan CopyRetry = TimeSpan.FromMilliseconds(50);

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

    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyAsync(ViewModel.Report);

    private async void OnReport(object sender, RoutedEventArgs e)
    {
        // The issue would only say to paste the report
        if (ViewModel.ToPaste is { } report && !await CopyAsync(report))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(ViewModel.GitHubLink) { UseShellExecute = true })?.Dispose();
    }

    private async Task<bool> CopyAsync(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        for (var tries = 1; ; tries++)
        {
            try
            {
                Clipboard.SetContent(package);
                CantCopyText.Visibility = Visibility.Collapsed;
                return true;
            }
            catch (COMException e) when (e.HResult == ClipboardBusy && tries < CopyTries)
            {
                await Task.Delay(CopyRetry);
            }
            catch (COMException e) when (e.HResult == ClipboardBusy)
            {
                CantCopyText.Visibility = Visibility.Visible;
                return false;
            }
        }
    }
}
