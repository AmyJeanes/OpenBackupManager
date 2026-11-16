using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace OpenBackupManager.App.Windows.UITests;

[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class QuitTests : AppTests
{
    [Test]
    public void Quit_AsksFirst_AndCancelKeepsItRunning()
    {
        WaitUntilConnected();
        ChooseFromTrayMenu("Quit");
        var question = WaitForQuitQuestion();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Text(question, "TitleText"), Is.EqualTo($"Quit {AppName}?"));
            Assert.That(Text(question, "DetailText"), Is.EqualTo("Any backup or sync in progress stops until you open the app again."));
        }

        Button(question, "CancelButton").Invoke();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Retry.WhileFalse(() => QuitQuestion() is null, Timeout, ignoreException: true).Result, Is.True, "The question should close");
            Assert.That(App.HasExited, Is.False);
            Assert.That(EngineStatus(), Is.Not.Null);
        }
    }

    [Test]
    public void DontAskAgain_IsSaved_SoTheNextQuitDoesnt()
    {
        ChooseFromTrayMenu("Quit");
        var question = WaitForQuitQuestion();
        question.FindFirstDescendant(c => c.ByAutomationId("DontAskAgainBox"))!.AsCheckBox().IsChecked = true;

        Button(question, "QuitButton").Invoke();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitForExit(), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(Home, "config.toml")), Does.Contain("ask-before-quitting = false"));
        }

        LaunchAgain();
        Quit(asks: false);
    }

    // The flyout stays, as signing out is heard through its window
    [Test]
    public void AltF4OnTheFlyout_Asks_AndAfterCancelTheTrayAndSigningOutStillWork()
    {
        NativeMethods.BringToFront(LaunchFlyout);
        Keyboard.TypeSimultaneously(VirtualKeyShort.ALT, VirtualKeyShort.F4);
        Button(WaitForQuitQuestion(), "CancelButton").Invoke();
        WaitUntilHidden(LaunchFlyout);

        Mouse.Click(TrayIcon());
        Assert.That(Retry.WhileFalse(IsFlyoutShown, Timeout).Result, Is.True, "The tray should open the flyout");

        NativeMethods.SignOut(LaunchFlyout);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitForExit(), Is.True);
            Assert.That(Log(), Does.Contain("Quitting, as Windows is signing out or shutting down"));
        }
    }

    // As taskkill without /f does, which a question would keep waiting
    [Test]
    public void ACloseFromAnotherProgram_QuitsWithoutAsking()
    {
        WaitUntilConnected();
        NativeMethods.Close(LaunchFlyout);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WaitForExit(), Is.True);
            Assert.That(Log(), Does.Contain("Quitting").And.Contain("Stopped the engine"));
        }
    }

    private static string? Text(Window window, string id) => window.FindFirstDescendant(c => c.ByAutomationId(id))?.Name;

    private static Button Button(Window window, string id) => window.FindFirstDescendant(c => c.ByAutomationId(id))!.AsButton();
}
