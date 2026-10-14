using Microsoft.Extensions.Logging.Abstractions;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Platform;

namespace OpenBackupManager.App.Windows.Tests;

public sealed class StartAtLoginSettingTests
{
    // Only a guard against a hang: the test waits for the watcher's own event
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private FakeStartAtLogin _startAtLogin = null!;
    private string _folder = "";
    private ConfigFile _file = null!;

    [SetUp]
    public void CreateFolder()
    {
        _startAtLogin = new FakeStartAtLogin();
        _folder = Directory.CreateTempSubdirectory("obm-login-").FullName;
        _file = new ConfigFile(Path.Combine(_folder, "config.toml"));
    }

    [TearDown]
    public void DeleteFolder() => Directory.Delete(_folder, recursive: true);

    [TestCase(true, ExpectedResult = true)]
    [TestCase(false, ExpectedResult = false)]
    public bool LeftOut_FollowsTheDefault(bool onByDefault)
    {
        using var watcher = new ConfigWatcher(_file);
        using var setting = Setting(watcher, onByDefault);

        return _startAtLogin.IsOn;
    }

    [TestCase("start-at-login = true", false, ExpectedResult = true)]
    [TestCase("start-at-login = false", true, ExpectedResult = false)]
    public bool TheSetting_WinsOverTheDefault(string text, bool onByDefault)
    {
        File.WriteAllText(_file.Path, text);
        using var watcher = new ConfigWatcher(_file);
        using var setting = Setting(watcher, onByDefault);

        return _startAtLogin.IsOn;
    }

    [Test]
    public void AChange_AppliesStraightAway()
    {
        using var watcher = new ConfigWatcher(_file);
        using var setting = Setting(watcher, onByDefault: true);
        // Raised after the setting's own handler, which was added first
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        watcher.Changed += _ => changed.TrySetResult();

        File.WriteAllText(_file.Path, "start-at-login = false");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(changed.Task.Wait(Timeout), Is.True, "The watcher didn't see the change");
            Assert.That(_startAtLogin.IsOn, Is.False);
        }
    }

    [Test]
    public void AFailure_IsOnlyLogged()
    {
        _startAtLogin.Fails = true;
        using var watcher = new ConfigWatcher(_file);

        Assert.DoesNotThrow(() => Setting(watcher, onByDefault: true).Dispose());
    }

    private StartAtLoginSetting Setting(ConfigWatcher watcher, bool onByDefault) =>
        new(watcher, _startAtLogin, onByDefault, NullLogger<StartAtLoginSetting>.Instance);

    private sealed class FakeStartAtLogin : IStartAtLogin
    {
        public bool IsOn { get; private set; }

        public bool Fails { get; set; }

        public void TurnOn() => Set(true);

        public void TurnOff() => Set(false);

        private void Set(bool on)
        {
            if (Fails)
            {
                throw new UnauthorizedAccessException();
            }

            IsOn = on;
        }
    }
}
