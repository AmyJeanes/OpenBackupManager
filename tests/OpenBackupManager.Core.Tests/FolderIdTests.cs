namespace OpenBackupManager.Core.Tests;

public class FolderIdTests
{
    [Test]
    public void New_IsEightLowercaseCrockfordCharacters()
    {
        var id = FolderId.New();

        Assert.That(id.Value, Does.Match("^[0-9a-hjkmnp-tv-z]{8}$"));
    }

    [Test]
    public void New_RoundTripsThroughTryParse()
    {
        var id = FolderId.New();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(FolderId.TryParse(id.ToString(), out var parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(id));
        }
    }

    [TestCase("0123abcd")]
    [TestCase("vwxyz789")]
    public void TryParse_AcceptsValidIds(string value)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(FolderId.TryParse(value, out var id), Is.True);
            Assert.That(id.Value, Is.EqualTo(value));
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("0123abc")]
    [TestCase("0123abcde")]
    [TestCase("0123ABCD")]
    [TestCase("0123abci")]
    [TestCase("0123abcl")]
    [TestCase("0123abco")]
    [TestCase("0123abcu")]
    public void TryParse_RejectsInvalidIds(string? value) => Assert.That(FolderId.TryParse(value, out _), Is.False);
}
