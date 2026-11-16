using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using OpenBackupManager.ViewModels;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

// Asks before the app quits, as the tray's Quit and Alt+F4 do
public sealed partial class QuitWindow : Window
{
    public QuitWindow(QuitViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        AppWindow.SetIcon(AppIcons.Ico);
        Title = AppTitleBar.Title = viewModel.AppName;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        AppWindow.SetPresenter(presenter);
        if (viewModel.Detail.Length == 0)
        {
            DetailText.Visibility = Visibility.Collapsed;
        }

        this.SetWindowSize(460, 240);
        this.CenterOnScreen();
        // So Enter quits, as the question asks
        QuitButton.Loaded += (_, _) => QuitButton.Focus(FocusState.Programmatic);
        FirstDraw.HideUntilDrawn(this);
    }

    public QuitViewModel ViewModel { get; }

    // Answered Quit, with whether to stop asking
    public event Action<bool>? Quitting;

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        Close();
        Quitting?.Invoke(DontAskAgainBox.IsChecked == true);
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => Close();
}
