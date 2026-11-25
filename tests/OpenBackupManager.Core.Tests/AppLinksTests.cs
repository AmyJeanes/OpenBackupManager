using System.Text.RegularExpressions;

namespace OpenBackupManager.Core.Tests;

public partial class AppLinksTests
{
    // So a link into the guide doesn't break if the guide moves, such as to a domain of its own
    [Test]
    public void GuideHome_IsWhereTheGuideIsPublished()
    {
        var toml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Docs", "zensical.toml"));

        Assert.That(AppLinks.GuideHome, Is.EqualTo(SiteUrl().Match(toml).Groups[1].Value));
    }

    [Test]
    public void Guide_IsAPageOfIt() =>
        Assert.That(AppLinks.Guide("docker/#moving-the-password"), Is.EqualTo("https://amyjeanes.github.io/OpenBackupManager/docker/#moving-the-password"));

    [Test]
    public void Repository_IsOnGitHub() =>
        Assert.That(AppLinks.Repository, Is.EqualTo("https://github.com/AmyJeanes/OpenBackupManager"));

    [GeneratedRegex("^site_url = \"([^\"]+)\"", RegexOptions.Multiline)]
    private static partial Regex SiteUrl();
}
