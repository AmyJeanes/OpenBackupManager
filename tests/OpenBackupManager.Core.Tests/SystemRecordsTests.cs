namespace OpenBackupManager.Core.Tests;

public class SystemRecordsTests
{
    private static readonly DateTimeOffset Since = new(2026, 10, 7, 16, 0, 0, TimeSpan.Zero);
    private static readonly string[] Exes = ["obmd.exe"];
    private static readonly string[] Folders = [@"C:\Users\Alice"];

    // The Application log is the whole PC's, and redaction only knows this user's name and folders
    [Test]
    public void Describe_KeepsOnlyThisAccountsEntries_AndSaysHowManyWereLeftOut()
    {
        WindowsEvent[] events =
        [
            new(Since.AddDays(1), CrashEvents.ApplicationError, 1000, 100, @"C:\Users\Alice\AppData\Local\OpenBackupManager.App\current\obmd.exe", "Alice's crash"),
            new(Since.AddDays(1), ".NET Runtime", 1026, 100, null, "Alice's stack"),
            new(Since.AddDays(2), CrashEvents.ApplicationError, 1000, 200, @"C:\Users\Alice Smith\AppData\Local\OpenBackupManager.App\current\obmd.exe", "Another Alice's crash"),
            new(Since.AddDays(2), ".NET Runtime", 1026, 200, null, "Another Alice's stack"),
            new(Since.AddDays(3), CrashEvents.ApplicationHang, 1002, 300, @"C:\Users\Bob\AppData\Local\OpenBackupManager.App\current\obmd.exe", "Bob's hang"),
        ];

        var text = SystemRecords.Describe(events, Exes, Folders, Since);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("Alice's crash").And.Contain("Alice's stack"));
            Assert.That(text, Does.Not.Contain("Another Alice's").And.Not.Contain("Bob's"));
            Assert.That(text, Does.Contain("3 other entries were left out, as they may be from other accounts"));
        }
    }

    [Test]
    public void Describe_SaysTheresNothing_WhenNoneAreThisAccounts()
    {
        WindowsEvent[] events = [new(Since.AddDays(1), ".NET Runtime", 1026, 100, null, "Someone's stack")];

        var text = SystemRecords.Describe(events, Exes, Folders, Since);

        Assert.That(text, Does.StartWith("There's nothing from obmd.exe since").And.Contain("1 other entry was left out").And.Not.Contain("Someone's"));
    }
}
