namespace OpenBackupManager.Core.Tests;

public class RedactionTests
{
    [Test]
    public void TakesOutTheHomeFolderAndNames_LeavingTheHeader() =>
        Assert.That(
            Redaction.Redact(
                Report("Can't reach ubuntu.local as amy", "Keeping its state in /home/amy/obm"),
                home: "/home/amy",
                user: "amy",
                computer: "ubuntu"),
            Is.EqualTo(Report("Can't reach <computer>.local as <user>", "Keeping its state in ~/obm")));

    [Test]
    public void TakesOutTheHomeFolder_WithEitherSlash() =>
        Assert.That(
            Redaction.Redact(Report(@"C:\Users\Amy\AppData and C:/Users/Amy/AppData"), @"C:\Users\Amy", "Amy", "DESKTOP"),
            Is.EqualTo(Report(@"~\AppData and ~/AppData")));

    [TestCase("Amy's folder", ExpectedResult = "<user>'s folder")]
    [TestCase("Amylase and amy2", ExpectedResult = "Amylase and amy2")]
    [TestCase("On AMY-PC", ExpectedResult = "On <computer>")]
    public string TakesOutNames_AsWholeWords(string line) =>
        Details(Redaction.Redact(Report(line), "/home/amy", "amy", "AMY-PC"));

    [Test]
    public void LeavesTheReportsOwnHeadings() =>
        Assert.That(
            Redaction.Redact(Report("macOS recorded this:", "Sent by obmd on macOS.local"), "/Users/amy", "amy", "macOS"),
            Is.EqualTo(Report("macOS recorded this:", "Sent by obmd on <computer>.local")));

    [Test]
    public void LeavesNamesUnder3Letters() =>
        Assert.That(Details(Redaction.Redact(Report("Waited a minute for pi"), "/home/pi", "pi", "a")), Is.EqualTo("Waited a minute for pi"));

    [TestCase("https://login.example.com/cb?code=abc&state=xyz", ExpectedResult = "https://login.example.com/cb?code=<removed>&state=<removed>")]
    [TestCase("(see https://example.com/a?token=abc)", ExpectedResult = "(see https://example.com/a?token=<removed>)")]
    [TestCase("https://example.com/a?abc123", ExpectedResult = "https://example.com/a?<removed>")]
    [TestCase("https://user:hunter2@example.com/path", ExpectedResult = "https://user:<removed>@example.com/path")]
    [TestCase("https://example.com:8080/path", ExpectedResult = "https://example.com:8080/path")]
    public string TakesOutEveryValueInAWebAddress(string line) =>
        Details(Redaction.Redact(Report(line), "/home/amy", "amy", "ubuntu"));

    private static string Report(params string[] details) => string.Join(
        "\n",
        ["obmd closed unexpectedly at 2026-11-05 00:13:54 +00:00", "", "Version: 1.2.3", "OS: Ubuntu 24.04.1 LTS (X64)", ".NET: .NET 10.0.0", "", .. details]);

    private static string Details(string report) => report.Split('\n')[6];
}
