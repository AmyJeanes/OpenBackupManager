using OpenBackupManager.Core;
using OpenBackupManager.Core.Configuration;
using OpenBackupManager.Core.Protocol;

namespace OpenBackupManager.Cli.Tests;

// Scripts read these names, so a change here breaks them, as one to the protocol would break the app
public class JsonTests
{
    [Test]
    public void Status_KeepsItsFieldNames()
    {
        var status = new StatusResult(
            "1.2.3",
            4321,
            new DateTimeOffset(2026, 11, 4, 9, 12, 44, TimeSpan.Zero),
            "/home/me/.config/openbackupmanager",
            "/home/me/.local/state/openbackupmanager",
            EngineStarter.Service,
            new SecretsStatus("file", "/home/me/.local/state/openbackupmanager/secrets.json"),
            [new ConfigProblem(0, ConfigProblemKind.ShouldBeTrueOrFalse, IsError: true, "updates.prereleases", "OBM_UPDATES_PRERELEASES")]);

        Assert.That(Json.Format(status, ProtocolJson.Default.StatusResult).ReplaceLineEndings(), Is.EqualTo("""
            {
              "version": "1.2.3",
              "processId": 4321,
              "started": "2026-11-04T09:12:44+00:00",
              "config": "/home/me/.config/openbackupmanager",
              "state": "/home/me/.local/state/openbackupmanager",
              "startedBy": "service",
              "secrets": {
                "store": "file",
                "file": "/home/me/.local/state/openbackupmanager/secrets.json"
              },
              "configProblems": [
                {
                  "line": 0,
                  "kind": "shouldBeTrueOrFalse",
                  "isError": true,
                  "value": "updates.prereleases",
                  "envVar": "OBM_UPDATES_PRERELEASES",
                  "message": "updates.prereleases should be true or false"
                }
              ]
            }
            """.ReplaceLineEndings()));
    }

    [Test]
    public void ConfigProblems_KeepTheirFieldNames() =>
        Assert.That(
            Json.Format(new ReloadConfigResult([new ConfigProblem(1, ConfigProblemKind.UnknownSetting, IsError: false, "colour")]), ProtocolJson.Default.ReloadConfigResult)
                .ReplaceLineEndings(),
            Is.EqualTo("""
                {
                  "problems": [
                    {
                      "line": 1,
                      "kind": "unknownSetting",
                      "isError": false,
                      "value": "colour",
                      "message": "colour isn't a known setting"
                    }
                  ]
                }
                """.ReplaceLineEndings()));

    [Test]
    public void CrashReports_KeepTheirFieldNames() =>
        Assert.That(
            Json.Format<IReadOnlyList<SavedReport>>(
                [new SavedReport(1, "engine", "/home/me/crash.txt", "obmd closed unexpectedly at 2026-11-04 09:12:03 +00:00", "Something broke", New: true)],
                CliJson.Default.IReadOnlyListSavedReport)
                .ReplaceLineEndings(),
            Is.EqualTo("""
                [
                  {
                    "number": 1,
                    "program": "engine",
                    "file": "/home/me/crash.txt",
                    "heading": "obmd closed unexpectedly at 2026-11-04 09:12:03 +00:00",
                    "summary": "Something broke",
                    "new": true
                  }
                ]
                """.ReplaceLineEndings()));
}
