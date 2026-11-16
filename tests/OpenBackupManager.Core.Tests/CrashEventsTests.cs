namespace OpenBackupManager.Core.Tests;

// Application Error's and Application Hang's data as Windows gives it
public class CrashEventsTests
{
    private const int ProcessId = 0x1a2c;

    [Test]
    public void IsOf_ApplicationError_OnWindows11() =>
        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationError, Error(ProcessId), "obmd.exe", ProcessId), Is.True);

    // Windows 10's are classic events, whose values are all text, with the process ID in hex without 0x
    [Test]
    public void IsOf_ApplicationError_OnWindows10() =>
        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationError, Error("1a2c"), "obmd.exe", ProcessId), Is.True);

    [Test]
    public void IsOf_ApplicationHang_OnWindows10() =>
        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationHang, Hang("1a2c"), "OpenBackupManager.App.Windows.exe", ProcessId), Is.True);

    [Test]
    public void IsOf_ApplicationHang_OnWindows11() =>
        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationHang, Hang(ProcessId), "OpenBackupManager.App.Windows.exe", ProcessId), Is.True);

    [Test]
    public void IsOf_TakesTheProcessIdFromItsPlace_NotFromTimestampsOrOffsets()
    {
        var values = Error("2b3d");
        values[2] = "1a2c";
        values[7] = "0x1a2c";

        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationError, values, "obmd.exe", ProcessId), Is.False);
    }

    [Test]
    public void IsOf_IsntAnotherProgramsEntry() =>
        Assert.That(CrashEvents.IsOf(CrashEvents.ApplicationError, Error(ProcessId), "obm.exe", ProcessId), Is.False);

    private static List<object?> Error(object processId) =>
    [
        "obmd.exe", "1.2.3.0", "68a1b2c3", "KERNELBASE.dll", "10.0.19041.5072", "5f3e2a10", "e0434352", "000000000003a839",
        processId, "01db2c3e5f6a7b8c", @"C:\Users\Someone\AppData\Local\OpenBackupManager.App\current\obmd.exe",
        @"C:\Windows\System32\KERNELBASE.dll", "6f1e8d2a-1b2c-4d3e-8f9a-0b1c2d3e4f5a", "", "",
    ];

    private static List<object?> Hang(object processId) =>
    [
        "OpenBackupManager.App.Windows.exe", "1.2.3.0", processId, "01db2c3e5f6a7b8c", "4294967295",
        @"C:\Users\Someone\AppData\Local\OpenBackupManager.App\current\OpenBackupManager.App.Windows.exe",
        "6f1e8d2a-1b2c-4d3e-8f9a-0b1c2d3e4f5a", "", "", "Quiesce",
    ];
}
