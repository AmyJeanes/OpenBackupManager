namespace OpenBackupManager.Core.Tests;

// macOS's crash reports, laid out as ReportCrash wrote them for obmd on macOS 15
public class DiagnosticReportsTests
{
    private const string Boot = "05BBC561-3C1E-4F7A-9C55-2D8E7B1A6F40";

    private static readonly Run Crashed = new(1161, new DateTimeOffset(2026, 11, 4, 2, 9, 12, TimeSpan.Zero), "1.2.3", null, 0, Boot);

    [Test]
    public void Describe_HasTheSignalAndTheTopOfTheStack()
    {
        var recorded = DiagnosticReports.Describe(Ips(1161, Boot, 2), Crashed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recorded?.Time, Is.EqualTo(new DateTimeOffset(2026, 11, 4, 2, 9, 14, 801, TimeSpan.Zero).AddTicks(2000)));
            Assert.That(recorded?.Text.ReplaceLineEndings(), Is.EqualTo("""
                EXC_CRASH (SIGABRT), sent by obmd[1161]
                abort() called
                Thread 1 crashed:
                 0  libsystem_kernel.dylib  __pthread_kill + 10
                 1  obmd  S_P_CoreLib_System_Environment__FailFast + 42 (Environment.NativeAot.cs:40)
                """.ReplaceLineEndings()));
        }
    }

    [Test]
    public void Describe_KeepsTheTopOfALongStack() =>
        Assert.That(
            DiagnosticReports.Describe(Ips(1161, Boot, 20), Crashed)?.Text.Split(Environment.NewLine)[^2..],
            Is.EqualTo(["14  obmd  0x3e8", "    and 5 more"]));

    // Apple's forums say the line of metadata is sometimes left out
    [Test]
    public void Describe_ReadsAReportWithNoMetadataLine()
    {
        var ips = Ips(1161, Boot, 2);

        Assert.That(DiagnosticReports.Describe(ips[(ips.IndexOf('}', StringComparison.Ordinal) + 2)..], Crashed)?.Text, Does.StartWith("EXC_CRASH (SIGABRT)"));
    }

    // Apple says a report can contain each of the rest, so one with only these still says what crashed it
    [Test]
    public void Describe_NeedsOnlyTheProcessTimeAndException() =>
        Assert.That(
            DiagnosticReports.Describe("""
                {"pid":1161,"captureTime":"2026-11-04 02:09:14.8012 +0000","exception":{"type":"EXC_CRASH","signal":"SIGABRT"}}
                """, Crashed)?.Text,
            Is.EqualTo("EXC_CRASH (SIGABRT)"));

    [Test]
    public void Describe_ThrowsJsonException_ForAReportWithoutAnException() =>
        Assert.That(
            () => DiagnosticReports.Describe("""{"pid":1161,"captureTime":"2026-11-04 02:09:14.8012 +0000"}""", Crashed),
            Throws.InstanceOf<System.Text.Json.JsonException>());

    // A process ID can be another process's after a restart
    [TestCase(1162, Boot)]
    [TestCase(1161, "999C9DF4-0B6A-4C31-8E2F-7A5D1C3B9E20")]
    public void Describe_PassesOverAnotherProcesssReport(int pid, string boot) =>
        Assert.That(DiagnosticReports.Describe(Ips(pid, boot, 2), Crashed), Is.Null);

    // The first two frames as macOS gave them for a FailFast, then frames with no symbol
    private static string Ips(int pid, string boot, int frames)
    {
        string[] known =
        [
            """{"imageOffset":30790,"symbol":"__pthread_kill","symbolLocation":10,"imageIndex":1}""",
            """{"imageOffset":2840298,"sourceLine":40,"sourceFile":"Environment.NativeAot.cs","symbol":"S_P_CoreLib_System_Environment__FailFast","imageIndex":0,"symbolLocation":42}""",
        ];
        var stack = string.Join(",", known.Concat(Enumerable.Range(2, Math.Max(0, frames - 2)).Select(_ => """{"imageOffset":1000,"imageIndex":0}""")).Take(frames));
        return $$"""
            {"app_name":"obmd","timestamp":"2026-11-04 02:09:15.00 +0000","bug_type":"309","name":"obmd"}
            {
              "captureTime" : "2026-11-04 02:09:14.8012 +0000",
              "pid" : {{pid}},
              "procLaunch" : "2026-11-04 02:09:12.9154 +0000",
              "procName" : "obmd",
              "bootSessionUUID" : "{{boot}}",
              "exception" : {"codes":"0x0000000000000000, 0x0000000000000000","rawCodes":[0,0],"type":"EXC_CRASH","signal":"SIGABRT"},
              "termination" : {"flags":0,"code":6,"namespace":"SIGNAL","indicator":"Abort trap: 6","byProc":"obmd","byPid":{{pid}}},
              "asi" : {"libsystem_c.dylib":["abort() called"]},
              "faultingThread" : 1,
              "threads" : [{"id":1,"frames":[]},{"triggered":true,"id":2,"frames":[{{stack}}]}],
              "usedImages" : [{"name":"obmd","path":"/Users/USER/*/obmd"},{"name":"libsystem_kernel.dylib"}]
            }
            """;
    }
}
