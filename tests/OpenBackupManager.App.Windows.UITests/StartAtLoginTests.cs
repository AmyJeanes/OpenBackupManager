using FlaUI.Core.Tools;
using Microsoft.Win32;

namespace OpenBackupManager.App.Windows.UITests;

[Explicit("Takes over the desktop")]
[Category("Sandbox")]
public sealed class StartAtLoginTests : AppTests
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // In case a test fails while it's on, so the test copy never starts at login
    [TearDown]
    public void RemoveRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (RunEntryName(key) is { } name)
        {
            key!.DeleteValue(name);
        }
    }

    [Test]
    public void ACopyThatWasntInstalled_DoesntStartAtLogin() => Assert.That(RunEntry(), Is.Null);

    [Test]
    public void TheSetting_AddsAndRemovesTheRunEntry()
    {
        WriteConfig("start-at-login = true");
        Assert.That(
            Retry.WhileNull(RunEntry, Timeout).Result,
            Is.EqualTo($"\"{AppPath}\" --home \"{Home}\" --at-login").IgnoreCase);

        WriteConfig("start-at-login = false");
        Assert.That(Retry.WhileNotNull(RunEntry, Timeout).Result, Is.True);
    }

    [Test]
    public void StartedAtLogin_StaysInTheTray()
    {
        Quit();

        StartAgain("--at-login");
        _ = TrayIcon();
        // The flyout opens as the icon appears when it's launched, so a few seconds without it is enough
        Assert.That(Retry.WhileFalse(IsFlyoutShown, TimeSpan.FromSeconds(3)).Result, Is.False);

        // A copy that's already running isn't asked to open it either
        using (var second = LaunchApp("--at-login"))
        {
            Assert.That(second.WaitForExit(Timeout), Is.True);
        }

        Assert.That(Retry.WhileFalse(IsFlyoutShown, TimeSpan.FromSeconds(3)).Result, Is.False);
    }

    private void WriteConfig(string text) => File.WriteAllText(Path.Combine(Home, "config.toml"), $"version = 1\n{text}\n");

    private string? RunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return RunEntryName(key) is { } name ? key!.GetValue(name) as string : null;
    }

    // Found by its home, since its name is made from it inside the app
    private string? RunEntryName(RegistryKey? key) =>
        key?.GetValueNames().FirstOrDefault(name =>
            key.GetValue(name) is string command && command.Contains($"--home \"{Home}\"", StringComparison.OrdinalIgnoreCase));
}
