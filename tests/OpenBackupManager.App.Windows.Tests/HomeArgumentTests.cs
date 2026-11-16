using System.Runtime.InteropServices;

namespace OpenBackupManager.App.Windows.Tests;

public class HomeArgumentTests
{
    [TestCase(@"C:\Some home")]
    [TestCase(@"E:\")]
    [TestCase(@"D:\OBM\")]
    [TestCase(@"\\server\share\")]
    public void WindowsReadsBack_TheSameHome_AndWhatFollows(string home) =>
        Assert.That(Split($"app.exe {Program.HomeArgument(home)} --at-login"), Is.EqualTo(["app.exe", "--home", home, "--at-login"]));

    // As Windows splits a command line into a program's arguments
    private static string[] Split(string commandLine)
    {
        var list = CommandLineToArgvW(commandLine, out var count);
        try
        {
            return [.. Enumerable.Range(0, count).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(list, i * IntPtr.Size))!)];
        }
        finally
        {
            LocalFree(list);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string commandLine, out int count);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
