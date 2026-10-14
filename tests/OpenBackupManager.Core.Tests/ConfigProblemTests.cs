using System.Globalization;
using OpenBackupManager.Core.Configuration;

namespace OpenBackupManager.Core.Tests;

public class ConfigProblemTests
{
    [Test]
    public void EveryKind_HasAMessage([Values] ConfigProblemKind kind) =>
        Assert.That(new ConfigProblem(1, kind, IsError: true, "a").Message(CultureInfo.InvariantCulture), Is.Not.Empty);

    [Test]
    public void Describe_NamesTheLine() =>
        Assert.That(
            new ConfigProblem(2, ConfigProblemKind.ShouldBeTrueOrFalse, IsError: true, "updates.prereleases").Describe(CultureInfo.InvariantCulture),
            Is.EqualTo("Line 2: updates.prereleases should be true or false"));

#if DEBUG
    [Test]
    public void Describe_UsesTheGivenLanguage_ButKeepsTheSettingsName()
    {
        var text = new ConfigProblem(2, ConfigProblemKind.ShouldBeTrueOrFalse, IsError: true, "updates.prereleases").Describe(CultureInfo.GetCultureInfo("qps-ploc"));

        Assert.That(text, Does.StartWith("[").And.Contains("updates.prereleases").And.Not.Contains("should be"));
    }

    [Test]
    public void TomlynsMessage_StaysAsItIs_InAnyLanguage() =>
        Assert.That(
            new ConfigProblem(2, ConfigProblemKind.Tomlyn, IsError: true, "Unexpected token `tru` for a value").Message(CultureInfo.GetCultureInfo("qps-ploc")),
            Is.EqualTo("Unexpected token `tru` for a value"));
#endif

    [Test]
    [SetUICulture("qps-ploc")]
    public void ToString_IsAlwaysInEnglish() =>
        Assert.That(new ConfigProblem(2, ConfigProblemKind.UnknownSetting, IsError: false, "colour").ToString(), Is.EqualTo("line 2: colour isn't a known setting"));
}
