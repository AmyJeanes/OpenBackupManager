using System.Globalization;
using System.Reflection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using WinUIEx;

namespace OpenBackupManager.App.Windows;

public sealed partial class AboutWindow : Window
{
    // A check that finds nothing can finish very quickly, which can look like the button did nothing
    private static readonly TimeSpan MinimumCheckTime = TimeSpan.FromMilliseconds(500);

    private readonly Updater _updater;
    private readonly Action _restartToUpdate;

    public AboutWindow(Updater updater, Action restartToUpdate)
    {
        InitializeComponent();
        _updater = updater;
        _restartToUpdate = restartToUpdate;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
        // Use a PNG for the Image control, as the .ico's first size that the Image control would display is too small
        Logo.Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.png")));
        Title = "About " + Program.AppName;
        NameText.Text = Program.AppName;
        VersionText.Text = "Version " + Version() + (updater.IsTestSource ? " (test updates)" : "");
        ShowUpdateButton();
        if (updater.CanUpdate)
        {
            UpdateStatusText.Text = UpdateStatus();
        }
        else
        {
            UpdateButton.IsEnabled = false;
            UpdateStatusText.Text = "Updates are unavailable on dev builds";
        }

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

    private string UpdateStatus()
    {
        if (_updater.UpdateReady)
        {
            return ReadyText();
        }

        if (_updater.LastChecked is not { } checkedAt)
        {
            return "Not checked yet";
        }

        var culture = CultureInfo.CurrentCulture;
        var time = checkedAt.ToString("t", culture);
        if (checkedAt.Date == DateTime.Today)
        {
            return $"Last checked at {time}";
        }

        if (checkedAt.Date == DateTime.Today.AddDays(-1))
        {
            return $"Last checked yesterday at {time}";
        }

        return $"Last checked on {checkedAt.ToString("M", culture)} at {time}";
    }

    // Strips build info e.g. commit hash from the displayed version
    private static string Version()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        return version.Split('+')[0];
    }

    private string ReadyText() => $"Version {_updater.ReadyVersion} is ready to install";

    // Restarts to install a waiting update, or checks for one
    private void ShowUpdateButton() => UpdateButton.Content = _updater.UpdateReady ? "Restart to update" : "Check for updates";

    private async void OnUpdateButton(object sender, RoutedEventArgs e)
    {
        if (_updater.UpdateReady)
        {
            _restartToUpdate();
            return;
        }

        UpdateButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking…";
        // Reported back on this thread
        IProgress<int> downloading = new Progress<int>(percent => UpdateStatusText.Text = $"Downloading update… {percent}%");
        var check = _updater.CheckNow(downloading.Report);
        await Task.WhenAll(check, Task.Delay(MinimumCheckTime));
        UpdateStatusText.Text = await check switch
        {
            Updater.CheckResult.UpdateReady => ReadyText(),
            Updater.CheckResult.UpToDate => "You're up to date",
            _ => "Couldn't check for updates",
        };
        ShowUpdateButton();
        UpdateButton.IsEnabled = true;
    }
}
