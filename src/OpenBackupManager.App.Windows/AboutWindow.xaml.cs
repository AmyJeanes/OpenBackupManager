using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using OpenBackupManager.ViewModels;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class AboutWindow : Window
{
    public AboutWindow(AboutViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.SetIcon(AppIcons.Ico);
        // Use a PNG for the Image control, as the .ico's first size that the Image control would display is too small
        Logo.Source = new BitmapImage(new Uri(AppIcons.Png));
        Title = viewModel.Title;
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        this.SetWindowSize(420, 340);
        this.CenterOnScreen();
        FirstDraw.HideUntilDrawn(this);
    }

    public AboutViewModel ViewModel { get; }
}
